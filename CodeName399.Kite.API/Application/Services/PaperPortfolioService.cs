using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Repositories.Interface;
namespace CodeName399.Kite.API.Services;

public sealed class PaperPortfolioService
{
    private readonly ITradeOrderRepository _repository; private readonly ITradingConfigurationRepository _configurationRepository; private readonly TradingSettingsProvider _settings; private readonly MarketClock _clock; private readonly LiveMarketCache _cache;
    private readonly object _balanceSync = new();
    private decimal? _availableBalance;
    private DateOnly _balanceDate;
    public PaperPortfolioService(ITradeOrderRepository repository, ITradingConfigurationRepository configurationRepository, TradingSettingsProvider settings, MarketClock clock, LiveMarketCache cache) { _repository = repository; _configurationRepository = configurationRepository; _settings = settings; _clock = clock; _cache = cache; }
    /// <summary>
    /// Maintains the simulated account balance in memory during the trading day.
    /// Manual buys reserve capital and sells credit proceeds net of estimated exit
    /// charges. Paper-as-live persists the remaining balance at market close.
    /// </summary>
    public Task<decimal> GetAvailableBalanceAsync()
    {
        lock (_balanceSync)
        {
            if (_availableBalance is null || _balanceDate != DateOnly.FromDateTime(_clock.Now))
            {
                _availableBalance = _settings.Current.PaperTradingBalance;
                _balanceDate = DateOnly.FromDateTime(_clock.Now);
            }
            return Task.FromResult(_availableBalance.Value);
        }
    }

    public bool TryReserveCapital(decimal amount)
    {
        if (amount < 0) return false;
        lock (_balanceSync)
        {
            if (_availableBalance is null || _balanceDate != DateOnly.FromDateTime(_clock.Now))
            {
                _availableBalance = _settings.Current.PaperTradingBalance;
                _balanceDate = DateOnly.FromDateTime(_clock.Now);
            }
            if (_availableBalance.Value < amount) return false;
            _availableBalance = _availableBalance.Value - amount;
            return true;
        }
    }

    public void CreditCapital(decimal amount)
    {
        if (amount <= 0) return;
        lock (_balanceSync)
        {
            if (_availableBalance is null || _balanceDate != DateOnly.FromDateTime(_clock.Now))
            {
                _availableBalance = _settings.Current.PaperTradingBalance;
                _balanceDate = DateOnly.FromDateTime(_clock.Now);
            }
            _availableBalance = _availableBalance.Value + amount;
        }
    }
    public async Task PersistBalanceAsync()
    {
        var balance = await GetAvailableBalanceAsync();
        var configuration = TradingSettingsProvider.Clone(_settings.Current);
        configuration.PaperTradingBalance = balance;
        await _configurationRepository.UpdateAsync(configuration);
        _settings.Update(configuration);
    }

    public async Task<decimal> GetReservedCapitalAsync() => (await _repository.GetOpenTradesAsync()).Where(x => x.ExecutionMode == TradeExecutionMode.Paper && !x.Closed).Sum(x => Math.Max(0, x.ReservedCapital));
    private static decimal NetPnl(TradeOrder x) { if (x.EntryPrice <= 0 || x.ExitPrice <= 0 || x.Quantity <= 0) return 0; var gross = x.Direction == TradeDirection.Short ? (x.EntryPrice - x.ExitPrice) : (x.ExitPrice - x.EntryPrice); return gross * x.Quantity - x.EstimatedCharges; }
}
