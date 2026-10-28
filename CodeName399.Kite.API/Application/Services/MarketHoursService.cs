using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Services;

public class MarketHoursService : IMarketHoursService
{
    private readonly TradingSettingsProvider _settings;
    private readonly MarketClock _clock;

    public MarketHoursService(TradingSettingsProvider settings, MarketClock clock)
    {
        _settings = settings;
        _clock = clock;
    }

    public bool IsOpen() => _clock.IsMarketOpen;

    public bool IsNewEntryOpen() => _clock.IsNewIntradayEntryWindowOpen;
}
