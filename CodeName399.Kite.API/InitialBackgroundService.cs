namespace CodeName399.Kite.API
{
    using CodeName399.Kite.API.Services.Interface;

    public class InitialBackgroundService : BackgroundService
    {
        private readonly ILogger<InitialBackgroundService> _logger;

        private readonly CommonService _commonService;

        private readonly IInstrumentService _instrumentService;

        private readonly IAngelOneService _angel;

        private int maxCandidates = 10;

        private string candleInterval = "ONE_MINUTE";

        private int candleBatchSize = 2;

        private int batchDelay = 1;

        public InitialBackgroundService(
            ILogger<InitialBackgroundService> logger,
            CommonService commonService,
                    IInstrumentService instrumentService,
        IAngelOneService angel)
        {
            _logger = logger;
            _commonService = commonService;
            _instrumentService = instrumentService;
            _angel = angel;
        }

        protected override async Task ExecuteAsync(
            CancellationToken stoppingToken)
        {
            _logger.LogInformation(
                "InitialBackgroundService started at {Time} IST",
                _commonService.GetIndianTime());

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var delay = _commonService.GetDelayUntilNextCandleClose();

                    await Task.Delay(delay, stoppingToken);

                    if (stoppingToken.IsCancellationRequested)
                        break;

                    var candleCloseTime = _commonService.GetIndianTime();

                    _logger.LogInformation(
                        "Processing candle closing at {CandleCloseTime} IST",
                        candleCloseTime.ToString("yyyy-MM-dd HH:mm:ss"));

                    var login = await _angel.GetLoginDataAsync();
                    var instruments = await _instrumentService.LoadInstruments();
                    var ownedStocks = await _commonService.GetOwnedStocksAsync();
                    var quotes = _commonService.GetTopQuotes(await _commonService.GetQuotesAsync(instruments, login.JwtToken), ownedStocks, maxCandidates);
                    var fromDate = new DateTime(
                        candleCloseTime.Year,
                        candleCloseTime.Month,
                        candleCloseTime.Day,
                        9, 15, 0,
                        DateTimeKind.Unspecified);

                   var candles = await _commonService.DownloadCandlesInBatchesAsync(
                        quotes,
                        instruments,
                        login.JwtToken,
                        candleInterval,
                        fromDate,
                        candleCloseTime,
                        candleBatchSize,
                        batchDelay);

                    await _commonService.PlaceBuyOrders(quotes.ToHashSet(),candles,login.JwtToken);
                }
                catch (OperationCanceledException)
                    when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Error during candle-close processing.");

                    await Task.Delay(
                        TimeSpan.FromSeconds(1),
                        stoppingToken);
                }
            }

            _logger.LogInformation(
                "InitialBackgroundService stopped at {Time} IST",
                _commonService.GetIndianTime());
        }
    }
}