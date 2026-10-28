using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.BackgroundServices
{
    public class SellTradingBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly WatchListService _watchList;
        private readonly TradingSettingsProvider _settings;
        private readonly ILogger<SellTradingBackgroundService> _logger;
        private readonly IMarketHoursService _marketHoursService;

        public SellTradingBackgroundService(
            IServiceProvider serviceProvider,
            WatchListService watchList,
            TradingSettingsProvider settings,
            IMarketHoursService marketHoursService,
            ILogger<SellTradingBackgroundService> logger)
        {
            _serviceProvider = serviceProvider;
            _watchList = watchList;
            _settings = settings;
            _marketHoursService = marketHoursService;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!_marketHoursService.IsOpen())
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        continue;
                    }

                    using var scope = _serviceProvider.CreateScope();

                    var sellService = scope.ServiceProvider.GetRequiredService<SellTradingService>();

                    const int maxCandidates = 10;
                    var gainers = _watchList.Current
                        .Where(x => x.IsOwned || x.InstrumentType == InstrumentType.Equity)
                        .Where(x => x.IsOwned || x.HistoricalLoaded)
                        .OrderByDescending(x => x.IsOwned)
                        .ThenByDescending(x => x.Priority)
                        .Take(maxCandidates + _watchList.Current.Count(x => x.IsOwned))
                        .ToList();

                    if (gainers.Count > 0)
                    {
                        await sellService.Execute(gainers);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Sell background service failed.");
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(_settings.Current.SellTradingInterval),
                    stoppingToken);
            }
        }
    }
}
