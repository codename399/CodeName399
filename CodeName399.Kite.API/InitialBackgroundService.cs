namespace CodeName399.Kite.API
{
    using CodeName399.Kite.API.Services;
    using CodeName399.Kite.API.Services.Interface;

    public class InitialBackgroundService : BackgroundService
    {
        private readonly ILogger<InitialBackgroundService> _logger;
        private readonly CommonService _commonService;
        private readonly IInstrumentService _instrumentService;
        private readonly IAngelOneService _angel;
        private readonly TradingSettingsProvider _settings;
        private readonly IMarketHoursService _marketHours;

        public InitialBackgroundService(
            ILogger<InitialBackgroundService> logger,
            CommonService commonService,
            IInstrumentService instrumentService,
            IAngelOneService angel,
            TradingSettingsProvider settings,
            IMarketHoursService marketHours)
        {
            _logger = logger;
            _commonService = commonService;
            _instrumentService = instrumentService;
            _angel = angel;
            _settings = settings;
            _marketHours = marketHours;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("InitialBackgroundService started at {Time} IST", _commonService.GetIndianTime());

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_commonService.GetDelayUntilNextCandleClose(), stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    var candleCloseTime = _commonService.GetIndianTime();
                    if (!_marketHours.IsOpen())
                    {
                        _logger.LogDebug("Skipping buy cycle: market is closed. Time={Time} IST", candleCloseTime);
                        continue;
                    }

                    // IsOpen controls the exchange session; IsNewEntryOpen additionally
                    // enforces the configured 30-minute intraday entry cutoff.
                    if (!_marketHours.IsNewEntryOpen())
                    {
                        _logger.LogInformation("Skipping buy cycle: intraday entry cutoff reached. Time={Time} IST", candleCloseTime);
                        continue;
                    }

                    _logger.LogInformation("Processing buy candle closing at {CandleCloseTime} IST", candleCloseTime.ToString("yyyy-MM-dd HH:mm:ss"));

                    var login = await _angel.GetLoginDataAsync();
                    var instruments = await _instrumentService.LoadInstruments();
                    var ownedStocks = await _commonService.GetOwnedStocksAsync();
                    var quotes = _commonService.GetTopQuotes(
                        await _commonService.GetQuotesAsync(instruments, login.JwtToken),
                        ownedStocks,
                        _settings.Current.MaxCandidates);

                    var fromDate = new DateTime(candleCloseTime.Year, candleCloseTime.Month, candleCloseTime.Day, 9, 15, 0, DateTimeKind.Unspecified);
                    var candles = await _commonService.DownloadCandlesInBatchesAsync(
                        quotes,
                        instruments,
                        login.JwtToken,
                        _settings.Current.CandleInterval,
                        fromDate,
                        candleCloseTime,
                        _settings.Current.CandleBatchSize,
                        _settings.Current.BatchDelay);

                    await _commonService.PlaceBuyOrders(quotes.ToHashSet(), candles, login.JwtToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during buy candle-close processing.");
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                }
            }

            _logger.LogInformation("InitialBackgroundService stopped at {Time} IST", _commonService.GetIndianTime());
        }
    }
}
