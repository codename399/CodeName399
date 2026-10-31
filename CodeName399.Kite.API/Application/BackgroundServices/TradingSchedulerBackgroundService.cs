using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.BackgroundServices;

public class TradingSchedulerBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly TradingStateService _state;
    private readonly TradingSettingsProvider _settingsProvider;
    private readonly MarketClock _clock;
    private readonly ILogger<TradingSchedulerBackgroundService> _logger;
    private DateTime _lastMarketCloseFinalizationDate = DateTime.MinValue;

    public TradingSchedulerBackgroundService(
        IServiceProvider serviceProvider,
        TradingStateService state,
        TradingSettingsProvider settingsProvider,
        MarketClock clock,
        ILogger<TradingSchedulerBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _state = state;
        _settingsProvider = settingsProvider;
        _clock = clock;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var settings = _settingsProvider.Current;
                var now = _clock.Now;
                var marketOpen = now.Date.Add(settings.MarketOpenTime);
                if (now >= marketOpen && _state.LastResetDate != now.Date)
                    _state.Reset();

                // ExitBackgroundService performs the configured five-minute-before-close
                // square-off. This scheduler retains only a market-close recovery barrier.

                var marketClose = now.Date.Add(_clock.GetSessionClose());

                if (now >= marketClose && _lastMarketCloseFinalizationDate != now.Date)
                {
                    using var closeScope = _serviceProvider.CreateScope();
                    await closeScope.ServiceProvider
                        .GetRequiredService<SquareOffService>()
                        .ExecuteAtMarketCloseAsync(stoppingToken);

                    _lastMarketCloseFinalizationDate = now.Date;
                }

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Trading Scheduler failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
