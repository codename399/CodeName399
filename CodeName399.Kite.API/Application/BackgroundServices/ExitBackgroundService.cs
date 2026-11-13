namespace CodeName399.Kite.API.Application.BackgroundServices
{
    using CodeName399.Kite.API.Application.Services;
    using CodeName399.Kite.API.Services;
    using CodeName399.Kite.API.Services.Interface;
    
    using CodeName399.Kite.API.Models;
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
        private readonly CandleBuilderTickClient _tickClient;
        private readonly ConcurrentDictionary<string, SemaphoreSlim> _tickLocks = new(StringComparer.OrdinalIgnoreCase);
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
            CandleBuilderTickClient tickClient)
        {
            _logger = logger;
            _commonService = commonService;
            _instrumentService = instrumentService;
            _angel = angel;
            _settings = settings;
            _marketHours = marketHours;
            _marketClock = marketClock;
            _scopeFactory = scopeFactory;
            _tickClient = tickClient;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ExitBackgroundService started at {Time} IST. Sell monitoring uses CandleBuilder SignalR ticks.", _marketClock.Now);
            _tickClient.TickReceived += OnTickReceived;
            try
            {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                        break;

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
                _tickClient.TickReceived -= OnTickReceived;
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
            var gate = _tickLocks.GetOrAdd(tick.SymbolToken, _ => new SemaphoreSlim(1, 1));
            if (!await gate.WaitAsync(0)) return;
            try
            {
                if (!_marketHours.IsOpen()) return;
                var owned = await _commonService.GetOwnedStocksAsync();
                var trade = owned.Values.FirstOrDefault(x => string.Equals(x.SymbolToken, tick.SymbolToken, StringComparison.OrdinalIgnoreCase));
                if (trade == null) return;
                await _commonService.ProcessSellTickAsync(trade, tick.Ltp, _marketClock.Now);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Live tick sell processing failed. Token={Token} LTP={Ltp}", tick.SymbolToken, tick.Ltp);
            }
            finally { gate.Release(); }
        }
    }
}
