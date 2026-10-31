namespace CodeName399.Kite.API
{
    using CodeName399.Kite.API.Services;
    using CodeName399.Kite.API.Services.Interface;

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
            IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _commonService = commonService;
            _instrumentService = instrumentService;
            _angel = angel;
            _settings = settings;
            _marketHours = marketHours;
            _marketClock = marketClock;
            _scopeFactory = scopeFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("ExitBackgroundService started at {Time} IST", _marketClock.Now);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_commonService.GetDelayUntilNextCandleClose(), stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    var now = _marketClock.Now;
                    var today = DateOnly.FromDateTime(now);

                    // Flatten all positions five minutes before the configured market close.
                    if (_marketClock.IsTradingDay
                        && _settings.Current.AutoSquareOff
                        && now >= _marketClock.AutoSquareOffTime
                        && _lastAutoSquareOffDate != today)
                    {
                        using var squareOffScope = _scopeFactory.CreateScope();
                        await squareOffScope.ServiceProvider
                            .GetRequiredService<SquareOffService>()
                            .ExecuteAtMarketCloseAsync(stoppingToken);
                        _lastAutoSquareOffDate = today;
                    }

                    // Send the daily summary after the market has closed. The summary
                    // service also persists its sent date, so retries are safe.
                    if (_marketClock.IsTradingDay
                        && now >= _marketClock.MarketClose
                        && _lastDailySummaryDate != today)
                    {
                        using var summaryScope = _scopeFactory.CreateScope();
                        var summaryService = summaryScope.ServiceProvider.GetRequiredService<IDailySummaryService>();
                        if (await summaryService.SendDailySummaryAsync())
                            _lastDailySummaryDate = today;
                    }

                    var candleCloseTime = now;
                    if (!_marketHours.IsOpen())
                    {
                        _logger.LogDebug("Skipping sell candle cycle: market is closed. Time={Time} IST", candleCloseTime);
                        continue;
                    }

                    _logger.LogInformation("Processing sell candle closing at {CandleCloseTime} IST", candleCloseTime.ToString("yyyy-MM-dd HH:mm:ss"));

                    var login = await _angel.GetLoginDataAsync();
                    var instruments = await _instrumentService.LoadInstruments();
                    var ownedStocks = await _commonService.GetOwnedStocksAsync();

                    if (ownedStocks.Count == 0)
                    {
                        _logger.LogInformation("No owned stocks found. Skipping candle download.");
                        continue;
                    }

                    var fromDate = new DateTime(candleCloseTime.Year, candleCloseTime.Month, candleCloseTime.Day, 9, 15, 0, DateTimeKind.Unspecified);
                    var candles = await _commonService.DownloadCandlesInBatchesAsync(
                        ownedStocks.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
                        instruments,
                        login.JwtToken,
                        _settings.Current.CandleInterval,
                        fromDate,
                        candleCloseTime,
                        _settings.Current.CandleBatchSize,
                        _settings.Current.BatchDelay);

                    await _commonService.PlaceSellOrder(ownedStocks, candles);
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

            _logger.LogInformation("ExitBackgroundService stopped at {Time} IST", _marketClock.Now);
        }
    }
}
