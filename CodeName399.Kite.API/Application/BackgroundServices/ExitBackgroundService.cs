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
        private readonly IInstrumentService _instrumentService;
        private readonly IAngelOneService _angel;
        private readonly TradingSettingsProvider _settings;
        private readonly IMarketHoursService _marketHours;
        private readonly MarketClock _marketClock;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAngelWebSocketClient _webSocket;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _tickLocks = new(StringComparer.OrdinalIgnoreCase);
        // Prevent target-tick and candle-close stop-loss handlers from submitting
        // overlapping exits for the same instrument.
        private readonly ConcurrentDictionary<string, byte> _sellInProgress = new(StringComparer.OrdinalIgnoreCase);

        // One in-progress one-minute candle per owned instrument. Exit decisions
        // are made only after this candle has fully closed.
        private readonly ConcurrentDictionary<string, Candle> _activeCandles =
            new(StringComparer.OrdinalIgnoreCase);

        private DateOnly? _lastAutoSquareOffDate;
        private DateOnly? _lastDailySummaryDate;

        public ExitBackgroundService(
            ILogger<ExitBackgroundService> logger,
            CommonService commonService,
            IInstrumentService instrumentService,
            IAngelOneService angel,
            TradingSettingsProvider settings,
            IMarketHoursService marketHours,
            MarketClock marketClock,
            IServiceScopeFactory scopeFactory,
            IAngelWebSocketClient webSocket)
        {
            _logger = logger;
            _commonService = commonService;
            _instrumentService = instrumentService;
            _angel = angel;
            _settings = settings;
            _marketHours = marketHours;
            _marketClock = marketClock;
            _scopeFactory = scopeFactory;
            _webSocket = webSocket;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ExitBackgroundService started at {Time} IST. Sell exits are evaluated only on completed one-minute candles.", _marketClock.Now);
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
            var token = tick.SymbolToken.Trim();
            var gate = _tickLocks.GetOrAdd(token, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();

            Candle? completedCandle = null;
            try
            {
                if (!_marketHours.IsOpen())
                    return;

                // Use the local exchange receive time for minute bucketing. This
                // avoids mixing UTC/local timestamps from different feed payloads.
                var now = _marketClock.Now;
                var minuteStart = new DateTime(now.Year, now.Month, now.Day,
                    now.Hour, now.Minute, 0, DateTimeKind.Unspecified);

                if (_activeCandles.TryGetValue(token, out var current))
                {
                    if (current.Timestamp < minuteStart)
                    {
                        if (_activeCandles.TryRemove(token, out var closed))
                            completedCandle = closed;

                        _activeCandles[token] = CreateCandle(minuteStart, tick.Ltp);
                    }
                    else if (current.Timestamp == minuteStart)
                    {
                        UpdateCandle(current, tick.Ltp);
                    }
                    else
                    {
                        // Ignore an out-of-order tick from an already completed
                        // minute; never mutate a newer candle with stale data.
                        _logger.LogDebug(
                            "EXIT TICK IGNORED: stale minute. Token={Token} TickMinute={TickMinute} ActiveMinute={ActiveMinute}",
                            token, minuteStart, current.Timestamp);
                    }
                }
                else
                {
                    _activeCandles[token] = CreateCandle(minuteStart, tick.Ltp);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to aggregate exit candle. Token={Token} LTP={Ltp}", token, tick.Ltp);
            }
            finally
            {
                gate.Release();
            }

            // Process the candle that just closed first (stop-loss decision), then
            // evaluate the current live tick for an immediate target exit.
            if (completedCandle != null)
                await EvaluateClosedCandleAsync(token, completedCandle);

            // Profit target is intentionally tick-driven: exit as soon as a live
            // tick reaches it, without waiting for the current candle to close.
            await EvaluateTargetTickAsync(token, tick);
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
            foreach (var item in _activeCandles.ToArray())
            {
                var token = item.Key;
                var gate = _tickLocks.GetOrAdd(token, _ => new SemaphoreSlim(1, 1));
                if (!await gate.WaitAsync(0))
                    continue;

                Candle? completed = null;
                try
                {
                    if (_activeCandles.TryGetValue(token, out var candle)
                        && candle.Timestamp.AddMinutes(1) <= now
                        && _activeCandles.TryRemove(token, out var removed))
                    {
                        completed = removed;
                    }
                }
                finally
                {
                    gate.Release();
                }

                if (completed != null)
                    await EvaluateClosedCandleAsync(token, completed);
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

        private static Candle CreateCandle(DateTime timestamp, decimal ltp) =>
            new()
            {
                Timestamp = timestamp,
                Open = ltp,
                High = ltp,
                Low = ltp,
                Close = ltp
            };

        private static void UpdateCandle(Candle candle, decimal ltp)
        {
            candle.High = Math.Max(candle.High, ltp);
            candle.Low = candle.Low <= 0m ? ltp : Math.Min(candle.Low, ltp);
            candle.Close = ltp;
        }
    }
}
