namespace CodeName399.Kite.API.Application.BackgroundServices
{
    using CodeName399.Kite.API.Application.Services;
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
        private readonly MarketQuotePublisher _quotePublisher;
        private readonly CandleFileStoreService _candleStore;

        public InitialBackgroundService(
            ILogger<InitialBackgroundService> logger,
            CommonService commonService,
            IInstrumentService instrumentService,
            IAngelOneService angel,
            TradingSettingsProvider settings,
            IMarketHoursService marketHours,
            MarketQuotePublisher quotePublisher,
            CandleFileStoreService candleStore)
        {
            _logger = logger;
            _commonService = commonService;
            _instrumentService = instrumentService;
            _angel = angel;
            _settings = settings;
            _marketHours = marketHours;
            _quotePublisher = quotePublisher;
            _candleStore = candleStore;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("InitialBackgroundService started at {Time} IST", _commonService.GetIndianTime());

            // Keep the candle schedule anchored to exchange minute boundaries.
            // Recomputing a one-minute delay after a long-running cycle caused
            // alternating candle closes to be skipped (the cycle itself takes
            // longer than one minute when quote batches are large).
            var nextCandleClose = _commonService.GetNextCandleCloseTime();

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delay = _commonService.GetDelayUntilCandleClose(nextCandleClose);
                    if (delay > TimeSpan.Zero)
                        await Task.Delay(delay, stoppingToken);
                    if (stoppingToken.IsCancellationRequested)
                        break;

                    var actualNow = _commonService.GetIndianTime();
                    var lag = actualNow - nextCandleClose;
                    if (lag > TimeSpan.FromSeconds(45))
                    {
                        // Do not replay stale entry checks with current LTP. Jump
                        // to the latest fully closed minute and report the missed
                        // closes explicitly. The quote scan is also parallelized
                        // so normal cycles should remain close to one minute.
                        var latestClosed = new DateTime(actualNow.Year, actualNow.Month, actualNow.Day,
                            actualNow.Hour, actualNow.Minute, 0, DateTimeKind.Unspecified);
                        if (latestClosed > nextCandleClose)
                        {
                            var skipped = (int)(latestClosed - nextCandleClose).TotalMinutes;
                            _logger.LogWarning("BUY CANDLE SCHEDULER LAG. Skipping {SkippedCandles} stale close(s). Scheduled={Scheduled} LatestClosed={LatestClosed} LagSeconds={LagSeconds:F1}",
                                skipped, nextCandleClose, latestClosed, lag.TotalSeconds);
                            nextCandleClose = latestClosed;
                        }
                    }

                    var candleCloseTime = nextCandleClose;
                    nextCandleClose = nextCandleClose.AddMinutes(1);
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

                    await _quotePublisher.PublishSnapshotAsync(quotes);

                    // Close and persist the completed tick-built interval before the
                    // strategy reads candles. Buy evaluation reads JSON files directly.
                    var candleInterval = string.IsNullOrWhiteSpace(_settings.Current.CandleInterval)
                        ? "ONE_MINUTE"
                        : _settings.Current.CandleInterval;
                    await _candleStore.FinalizeClosedCandlesAsync(
                        candleInterval, _commonService.GetIndianTime(), stoppingToken);
                    var candles = await _commonService.GetCandlesFromFilesForQuotesAsync(
                        quotes, instruments, candleInterval);

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
