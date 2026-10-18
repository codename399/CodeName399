using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using System.Collections.Concurrent;

namespace CodeName399.Kite.API.Services;

public sealed class IntradayRiskGuard
{
    private readonly TradeManager _tradeManager;
    private readonly ITradeOrderRepository _repository;
    private readonly TradingSettingsProvider _settings;
    private readonly MarketClock _clock;
    private readonly TradingStateService _state;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, (DateTimeOffset Expires, decimal Risk)> _reservations = new(StringComparer.OrdinalIgnoreCase);

    public IntradayRiskGuard(
        TradeManager tradeManager,
        ITradeOrderRepository repository,
        TradingSettingsProvider settings,
        MarketClock clock,
        TradingStateService state)
    {
        _tradeManager = tradeManager;
        _repository = repository;
        _settings = settings;
        _clock = clock;
        _state = state;
    }

    public Task<bool> CanOpenAsync(Gainer candidate, decimal estimatedRisk = 0m) =>
        TryReserveAsync(candidate, estimatedRisk);

    public async Task<decimal> GetAvailableRiskCapacityAsync()
    {
        await _gate.WaitAsync();
        try
        {
            CleanupReservations();
            if (_settings.Current.MaximumTotalOpenRisk <= 0m)
                return decimal.MaxValue;

            var openRisk = _tradeManager.GetOpen()
                .Where(x => x.InstrumentType == InstrumentType.Equity)
                .Sum(Risk);
            var reservedRisk = _reservations.Sum(x => x.Value.Risk);
            return Math.Max(0m, _settings.Current.MaximumTotalOpenRisk - openRisk - reservedRisk);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryReserveAsync(Gainer candidate, decimal estimatedRisk)
    {
        await _gate.WaitAsync();
        try
        {
            CleanupReservations();

            if (candidate.Direction != TradeDirection.Long)
                return false;

            if (!_state.CanOpenNewEntry(_settings.Current))
                return false;

            var cfg = _settings.Current.Equity;
            var open = _tradeManager.GetOpen().Where(x => x.InstrumentType == InstrumentType.Equity).ToList();

            var sameSymbol = open.Count(x => string.Equals(x.Symbol, candidate.Symbol, StringComparison.OrdinalIgnoreCase));
            if (sameSymbol > 0)
                return false;

            if (_settings.Current.MaximumTotalOpenRisk > 0 &&
                open.Sum(Risk) + _reservations.Sum(x => x.Value.Risk) + estimatedRisk > _settings.Current.MaximumTotalOpenRisk)
                return false;

            var key = $"{candidate.Exchange}:{candidate.SymbolToken}";
            _reservations[key] = (
                DateTimeOffset.UtcNow.AddSeconds(Math.Max(1, _settings.Current.RiskReservationSeconds)),
                Math.Max(0, estimatedRisk));

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> AdjustReservationAsync(Gainer candidate, decimal actualRisk)
    {
        await _gate.WaitAsync();
        try
        {
            var key = $"{candidate.Exchange}:{candidate.SymbolToken}";
            if (!_reservations.ContainsKey(key))
                return false;

            var open = _tradeManager.GetOpen()
                .Where(x => !string.Equals(x.SymbolToken, candidate.SymbolToken, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (_settings.Current.MaximumTotalOpenRisk > 0 &&
                open.Sum(Risk) + _reservations.Where(x => x.Key != key).Sum(x => x.Value.Risk) + actualRisk >
                _settings.Current.MaximumTotalOpenRisk)
                return false;

            _reservations[key] = (_reservations[key].Expires, Math.Max(0, actualRisk));
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Release(string exchange, string token, string underlying) =>
        _reservations.TryRemove($"{exchange}:{token}", out _);

    private void CleanupReservations()
    {
        foreach (var key in _reservations
            .Where(x => x.Value.Expires <= DateTimeOffset.UtcNow)
            .Select(x => x.Key)
            .ToList())
            _reservations.TryRemove(key, out _);
    }

    private static decimal Risk(TradeOrder x)
    {
        var grossRisk = Math.Abs(x.InitialRiskPerShare * x.Quantity);
        var charges = Math.Max(0m, x.EstimatedCharges);
        return grossRisk + charges;
    }

    private static decimal CalculateNetPnl(TradeOrder x) =>
        x.ExitPrice <= 0 || x.EntryPrice <= 0 || x.Quantity <= 0
            ? 0
            : (x.ExitPrice - x.EntryPrice) * x.Quantity - x.EstimatedCharges;

    private static decimal CalculateUnrealized(TradeOrder x)
    {
        var current = x.CurrentPrice > 0 ? x.CurrentPrice : x.EntryPrice;
        return (current - x.EntryPrice) * x.Quantity;
    }
}
