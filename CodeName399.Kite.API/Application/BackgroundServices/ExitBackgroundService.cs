namespace CodeName399.Kite.API.Application.BackgroundServices
{
    using CodeName399.Kite.API.Application.Services;
    using CodeName399.Kite.API.Services;
    using CodeName399.Kite.API.Services.Interface;
    using CodeName399.Kite.API.WebSocket.Interface;
    using CodeName399.Kite.API.Models;
    using CodeName399.Kite.API.Models.Indicator;
    using System.Collections.Concurrent;

    public class ExitBackgroundService : BackgroundService
    {
        private readonly ILogger<ExitBackgroundService> _logger;
        private readonly CommonService _commonService;
        private readonly TradingSettingsProvider _settings;
        private readonly IMarketHoursService _marketHours;
        private readonly MarketClock _marketClock;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAngelWebSocketClient _webSocket;
        // Prevent target-tick and candle-close stop-loss handlers from submitting
        // overlapping exits for the same instrument.
        private readonly ConcurrentDictionary<string, byte> _sellInProgress = new(StringComparer.OrdinalIgnoreCase);

        // Tracks the last CandleBuilder candle processed for each owned instrument.
        private readonly ConcurrentDictionary<string, DateTime> _lastProcessedCandleByToken =
            new(StringComparer.OrdinalIgnoreCase);

        private DateOnly? _lastAutoSquareOffDate;
        private DateOnly? _lastDailySummaryDate;

        public ExitBackgroundService(
            ILogger<ExitBackgroundService> logger,
            CommonService commonService,
            TradingSettingsProvider settings,
            IMarketHoursService marketHours,
            MarketClock marketClock,
            IServiceScopeFactory scopeFactory,
            IAngelWebSocketClient webSocket)
        {
            _logger = logger;
            _commonService = commonService;
            _settings = settings;
            _marketHours = marketHours;
            _marketClock = marketClock;
            _scopeFactory = scopeFactory;
            _webSocket = webSocket;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ExitBackgroundService started at {Time} IST. Candle-close exits consume finalized CandleBuilder files.", _marketClock.Now);
            _webSocket.TickReceived += OnTickReceived;
            try
            {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    // Finalize candles on the minute boundary even when no new
                    // tick arrives immediately after the boundary.
                    await FinalizeClosedCandlesAsync(_marketClock.Now);

                    var now = _marketClock.Now;
                    var today = DateOnly.FromDateTime(now);

                    if (_marketClock.IsTradingDay && _settings.Current.AutoSquareOff
                        && now >= _marketClock.AutoSquareOffTime && _lastAutoSquareOffDate != today)
                    {
                        using var squareOffScope = _scopeFactory.CreateScope();
                        await squareOffScope.ServiceProvider.GetRequiredService<SquareOffService>()
                            .ExecuteAtMarketCloseAsync(stoppingToken);
                        _lastAutoSquareOffDate = today;
                    }

                    if (_marketClock.IsTradingDay && now >= _marketClock.MarketClose && _lastDailySummaryDate != today)
                    {
                        using var summaryScope = _scopeFactory.CreateScope();
                        var summaryService = summaryScope.ServiceProvider.GetRequiredService<IDailySummaryService>();
                        if (await summaryService.SendDailySummaryAsync()) _lastDailySummaryDate = today;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during exit candle-close processing.");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }
            }
            finally
            {
                _webSocket.TickReceived -= OnTickReceived;
            }

            _logger.LogInformation("ExitBackgroundService stopped at {Time} IST", _marketClock.Now);
        }

        private void OnTickReceived(object? sender, MarketTick tick)
        {
            if (tick == null || string.IsNullOrWhiteSpace(tick.SymbolToken) || tick.Ltp <= 0m)
                return;

            _ = ProcessTickAsync(tick);
        }

        private async Task ProcessTickAsync(MarketTick tick)
        {
            try
            {
                if (!_marketHours.IsOpen())
                    return;

                await EvaluateTargetTickAsync(tick.SymbolToken.Trim(), tick);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Live target-tick processing failed. Token={Token} LTP={Ltp}", tick.SymbolToken, tick.Ltp);
            }
        }

        private async Task EvaluateTargetTickAsync(string token, MarketTick tick)
        {
            try
            {
                var owned = await _commonService.GetOwnedStocksAsync();
                var trade = owned.Values.FirstOrDefault(x =>
                    string.Equals(x.SymbolToken, token, StringComparison.OrdinalIgnoreCase));
                if (trade == null || trade.TargetPrice <= 0m || tick.Ltp < trade.TargetPrice)
                    return;

                if (!_sellInProgress.TryAdd(token, 0))
                    return;

                try
                {
                    await _commonService.ProcessSellTargetTickAsync(trade, tick.Ltp, _marketClock.Now);
                }
                finally
                {
                    _sellInProgress.TryRemove(token, out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Live target-tick sell evaluation failed. Token={Token} LTP={Ltp}", token, tick.Ltp);
            }
        }

        private async Task FinalizeClosedCandlesAsync(DateTime now)
        {
            var owned = await _commonService.GetOwnedStocksAsync();
            if (owned.Count == 0)
                return;

            var interval = string.IsNullOrWhiteSpace(_settings.Current.CandleInterval)
                ? "ONE_MINUTE"
                : _settings.Current.CandleInterval;
            var candlesByToken = await _commonService.LoadOwnedCandlesFromCandleBuilderAsync(
                owned, interval);

            foreach (var trade in owned.Values)
            {
                var token = trade.SymbolToken?.Trim();
                if (string.IsNullOrWhiteSpace(token) ||
                    !candlesByToken.TryGetValue(token, out var candles) ||
                    candles.Count == 0)
                    continue;

                // On service start, establish the latest persisted candle as the baseline.
                // This prevents stale candles from earlier sessions from triggering exits.
                if (!_lastProcessedCandleByToken.TryGetValue(token, out var lastProcessed))
                {
                    _lastProcessedCandleByToken[token] = candles[^1].Timestamp;
                    continue;
                }

                foreach (var candle in candles.Where(c => c.Timestamp > lastProcessed).OrderBy(c => c.Timestamp))
                {
                    if (candle.Timestamp.Add(CandleFileStoreService.IntervalDuration(interval)) > now)
                        continue;

                    await EvaluateClosedCandleAsync(token, candle);
                    _lastProcessedCandleByToken[token] = candle.Timestamp;
                }
            }
        }

        private async Task EvaluateClosedCandleAsync(string token, Candle candle)
        {
            try
            {
                var owned = await _commonService.GetOwnedStocksAsync();
                var trade = owned.Values.FirstOrDefault(x =>
                    string.Equals(x.SymbolToken, token, StringComparison.OrdinalIgnoreCase));
                if (trade == null)
                    return;

                if (!_sellInProgress.TryAdd(token, 0))
                    return;

                try
                {
                    await _commonService.ProcessSellCandleAsync(trade, candle);
                }
                finally
                {
                    _sellInProgress.TryRemove(token, out _);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Closed-candle sell evaluation failed. Token={Token} CandleTime={CandleTime} Close={Close}",
                    token, candle.Timestamp, candle.Close);
            }
        }

    }
}
