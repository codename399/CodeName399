using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
namespace CodeName399.Kite.API.Services;

public sealed class PaperPortfolioService
{
    private readonly TradingSettingsProvider _settings;
    public PaperPortfolioService(TradingSettingsProvider settings) { _settings = settings; }
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
}
