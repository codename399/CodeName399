using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
namespace CodeName399.Kite.API.Services;

public sealed class PaperPortfolioService
{
    private readonly ITradeOrderFileRepository _repository; private readonly TradingSettingsProvider _settings; private readonly MarketClock _clock; private readonly LiveMarketCache _cache;
    public PaperPortfolioService(ITradeOrderFileRepository repository, TradingSettingsProvider settings, MarketClock clock, LiveMarketCache cache) { _repository = repository; _settings = settings; _clock = clock; _cache = cache; }
    /// <summary>
    /// Returns the configured paper-trading balance without applying simulated
    /// realized/unrealized P&amp;L or open-trade reservations. Paper trading uses
    /// this value as a fixed capital baseline so autonomous configuration tests
    /// are all evaluated against the same configured account size.
    ///
    /// Trade affordability and risk limits remain enforced by the existing
    /// BuyTradingService and IntradayRiskGuard flows. Reserved capital is still
    /// available separately through GetReservedCapitalAsync().
    /// </summary>
    public Task<decimal> GetAvailableBalanceAsync()
    {
        return Task.FromResult(_settings.Current.PaperTradingBalance);
    }
    public async Task<decimal> GetReservedCapitalAsync() => (await _repository.GetOpenTradesAsync()).Where(x => x.ExecutionMode == TradeExecutionMode.Paper && !x.Closed).Sum(x => Math.Max(0, x.ReservedCapital));
    private static decimal NetPnl(TradeOrder x) { if (x.EntryPrice <= 0 || x.ExitPrice <= 0 || x.Quantity <= 0) return 0; var gross = x.Direction == TradeDirection.Short ? (x.EntryPrice - x.ExitPrice) : (x.ExitPrice - x.EntryPrice); return gross * x.Quantity - x.EstimatedCharges; }
}
