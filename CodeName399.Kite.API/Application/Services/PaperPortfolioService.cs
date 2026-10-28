using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
namespace CodeName399.Kite.API.Services;

public sealed class PaperPortfolioService
{
    private readonly ITradeOrderRepository _repository; private readonly TradingSettingsProvider _settings; private readonly MarketClock _clock; private readonly LiveMarketCache _cache;
    public PaperPortfolioService(ITradeOrderRepository repository, TradingSettingsProvider settings, MarketClock clock, LiveMarketCache cache) { _repository = repository; _settings = settings; _clock = clock; _cache = cache; }
    /// <summary>
    /// Keeps the paper account's available balance reduced by capital committed
    /// to open paper positions. Trade/order records remain persisted by the
    /// repository; the balance itself is calculated in memory from the configured
    /// baseline and currently open positions.
    /// </summary>
    public async Task<decimal> GetAvailableBalanceAsync()
    {
        var baseline = Math.Max(0m, _settings.Current.PaperTradingBalance);
        var reserved = await GetReservedCapitalAsync();
        return Math.Max(0m, baseline - reserved);
    }

    public async Task<decimal> GetReservedCapitalAsync() =>
        (await _repository.GetOpenTradesAsync())
            .Where(x => x.ExecutionMode == TradeExecutionMode.Paper && !x.Closed)
            .Sum(x => Math.Max(0m, x.ReservedCapital));
    private static decimal NetPnl(TradeOrder x) { if (x.EntryPrice <= 0 || x.ExitPrice <= 0 || x.Quantity <= 0) return 0; var gross = x.Direction == TradeDirection.Short ? (x.EntryPrice - x.ExitPrice) : (x.ExitPrice - x.EntryPrice); return gross * x.Quantity - x.EstimatedCharges; }
}
