using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Interface;
using CodeName399.Kite.API.SignalRHubs;
using Microsoft.AspNetCore.SignalR;

namespace CodeName399.Kite.API.BackgroundServices
{
    public class BuyTradingBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly WatchListService _watchList;
        private readonly IHubContext<MarketHub> _hub;
        private readonly TradingSettingsProvider _settings;
        private readonly ILogger<BuyTradingBackgroundService> _logger;
        private readonly IMarketHoursService _marketHoursService;

        public BuyTradingBackgroundService(
            IServiceProvider serviceProvider,
            WatchListService watchList,
            IHubContext<MarketHub> hub,
            TradingSettingsProvider settings,
            IMarketHoursService marketHoursService,
            ILogger<BuyTradingBackgroundService> logger
        )
        {
            _serviceProvider = serviceProvider;
            _watchList = watchList;
            _hub = hub;
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
                    if (!_marketHoursService.IsNewEntryOpen())
                    {
                        _logger.LogDebug("ORDER WORKER IDLE Gate=MarketHours Reason=BuyTradingService not invoked because entry window is closed.");
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        continue;
                    }

                    using var scope = _serviceProvider.CreateScope();

                    var buyService = scope.ServiceProvider.GetRequiredService<BuyTradingService>();

                    _logger.LogDebug("ORDER WORKER CYCLE Starting BuyTradingService.Execute");
                    // BuyTradingService should decide which stocks are eligible.
                    await buyService.Execute();
                    _logger.LogDebug("ORDER WORKER CYCLE Finished BuyTradingService.Execute");

                    await _hub.Clients.All.SendAsync(
                        "GainersUpdated",
                        _watchList.Current,
                        stoppingToken
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "ORDER WORKER FAILED Buy background service iteration failed before/while processing entries.");
                }

                await Task.Delay(
                    TimeSpan.FromMilliseconds(_settings.Current.BuyTradingInterval),
                    stoppingToken
                );
            }
        }
    }
}
