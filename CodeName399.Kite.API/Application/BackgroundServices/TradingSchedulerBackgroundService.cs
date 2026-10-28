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
    private DateTime _lastSummaryDate = DateTime.MinValue;
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
                var summaryTime = now.Date.Add(_clock.GetSessionClose()).AddMinutes(5);

                if (now >= marketOpen && _state.LastResetDate != now.Date)
                    _state.Reset();

                // Run the square-off coordinator every minute after the earliest
                // broker cutoff. It evaluates each open trade using the trade's
                // persisted instrument type, so changing the global instrument

                var earliestSquareOff = now.Date.Add(settings.RoboAutoSquareOffTime);
                if (settings.AutoSquareOff && now >= earliestSquareOff && now <= summaryTime)
                {
                    using var scope = _serviceProvider.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<SquareOffService>().Execute();
                }

                var marketClose = now.Date.Add(_clock.GetSessionClose());

                if (now >= marketClose && _lastMarketCloseFinalizationDate != now.Date)
                {
                    using var closeScope = _serviceProvider.CreateScope();
                    await closeScope.ServiceProvider
                        .GetRequiredService<SquareOffService>()
                        .ExecuteAtMarketCloseAsync(stoppingToken);

                    _lastMarketCloseFinalizationDate = now.Date;
                }

                if (_clock.IsTradingDay && now >= summaryTime && _lastSummaryDate != now.Date)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var summaryService = scope.ServiceProvider.GetRequiredService<IDailySummaryService>();
                    var sent = await summaryService.SendDailySummaryAsync();

                    if (sent)
                    {
                        _lastSummaryDate = now.Date;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "Daily summary was not sent for {Date}; it will be retried on the next scheduler cycle.",
                            now.Date
                        );
                    }
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
