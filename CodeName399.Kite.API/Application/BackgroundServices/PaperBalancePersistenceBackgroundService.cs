using CodeName399.Kite.API.Services;

namespace CodeName399.Kite.API.BackgroundServices;

/// <summary>Persists the simulated account balance once after the configured IST market close.</summary>
public sealed class PaperBalancePersistenceBackgroundService : BackgroundService
{
    private readonly PaperPortfolioService _portfolio;
    private readonly TradingSettingsProvider _settings;
    private readonly MarketClock _clock;
    private readonly ILogger<PaperBalancePersistenceBackgroundService> _logger;
    private DateOnly? _persistedDate;

    public PaperBalancePersistenceBackgroundService(PaperPortfolioService portfolio, TradingSettingsProvider settings, MarketClock clock, ILogger<PaperBalancePersistenceBackgroundService> logger)
    {
        _portfolio = portfolio; _settings = settings; _clock = clock; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var now = _clock.Now;
                var date = DateOnly.FromDateTime(now);
                if (_settings.Current.PaperTradingAsLive && _clock.IsTradingDay && now.TimeOfDay >= _settings.Current.MarketCloseTime && _persistedDate != date)
                {
                    await _portfolio.PersistBalanceAsync();
                    _persistedDate = date;
                    _logger.LogInformation("Paper-as-live balance persisted at market close. Date={Date}, Balance={Balance}", date, await _portfolio.GetAvailableBalanceAsync());
                }
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist paper-as-live balance.");
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
        }
    }
}
