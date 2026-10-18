using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Services;

public sealed class MarketHoursService : IMarketHoursService
{
    private readonly MarketClock _clock;

    public MarketHoursService(MarketClock clock) => _clock = clock;

    // Status display only. Order enforcement lives on the explicit buy/sell endpoints.
    public bool IsOpen() => _clock.IsMarketOpen;
}
