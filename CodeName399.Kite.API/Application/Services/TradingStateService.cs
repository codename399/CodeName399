using CodeName399.Kite.API.Models.Configuration;
using System.Text.Json;

namespace CodeName399.Kite.API.Services;

public sealed class TradingStateService
{
    private readonly object _lock = new();
    private readonly ILogger<TradingStateService> _logger;
    private readonly MarketClock _clock;
    private readonly string _stateFile = Path.Combine(AppContext.BaseDirectory, "Data", "trading-state.json");
    public int DailyTrades { get; private set; }
    public decimal DailyProfit { get; private set; }
    public decimal DailyLoss { get; private set; }
    public decimal NetPnL => DailyProfit - DailyLoss;
    public bool KillSwitch { get; private set; }
    public string KillSwitchReason { get; private set; } = "";
    public DateTime LastResetDate { get; private set; } = DateTime.MinValue;
    private bool _loaded;
    public TradingStateService(ILogger<TradingStateService> logger, MarketClock clock) { _logger = logger; _clock = clock; Load(); }
    public bool CanTrade(TradingConfiguration settings) => CanOpenNewEntry(settings);
    public bool CanOpenNewEntry(TradingConfiguration settings) { lock (_lock) { ResetIfNeeded(); return !KillSwitch; } }
    public void RegisterTrade() { lock (_lock) { ResetIfNeeded(); DailyTrades++; Persist(); } }
    public void RegisterTradeResult(decimal pnl, TradingConfiguration settings) { lock (_lock) { ResetIfNeeded(); if (pnl >= 0) DailyProfit += pnl; else DailyLoss += Math.Abs(pnl); Persist(); } }


    public void ActivateKillSwitch() { lock (_lock) ActivateUnsafe("Manual kill switch"); }
    public void ActivateForSession(string reason) { lock (_lock) ActivateUnsafe(reason); }
    private void ActivateUnsafe(string reason) { KillSwitch = true; KillSwitchReason = reason; _logger.LogWarning("Trading kill switch activated: {Reason}", reason); Persist(); }
    public void Reset() { lock (_lock) { DailyTrades = 0; DailyProfit = 0; DailyLoss = 0; KillSwitch = false; KillSwitchReason = ""; LastResetDate = _clock.Today; Persist(); } }
    private void ResetIfNeeded() { if (LastResetDate.Date != _clock.Today.Date) { DailyTrades = 0; DailyProfit = 0; DailyLoss = 0; KillSwitch = false; KillSwitchReason = ""; LastResetDate = _clock.Today; Persist(); } }
    private void Load() { if (_loaded) return; _loaded = true; try { if (!File.Exists(_stateFile)) return; var state = JsonSerializer.Deserialize<State>(File.ReadAllText(_stateFile)); if (state == null || state.Date != _clock.Today) return; DailyTrades = state.DailyTrades; DailyProfit = state.DailyProfit; DailyLoss = state.DailyLoss; KillSwitch = state.KillSwitch; KillSwitchReason = state.KillSwitchReason; LastResetDate = state.Date; } catch (Exception ex) { _logger.LogWarning(ex, "Unable to load trading state."); } }
    private void Persist() { try { var dir = Path.GetDirectoryName(_stateFile); if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir); File.WriteAllText(_stateFile, JsonSerializer.Serialize(new State(_clock.Today, DailyTrades, DailyProfit, DailyLoss, KillSwitch, KillSwitchReason))); } catch (Exception ex) { _logger.LogWarning(ex, "Unable to persist trading state."); } }
    private sealed record State(DateTime Date, int DailyTrades, decimal DailyProfit, decimal DailyLoss, bool KillSwitch, string KillSwitchReason);

    public void RegisterBrokerFailure(TradingConfiguration settings)
    {
        // Broker-failure kill-switch policy is intentionally not enabled by this
        // minimal configuration. Keep this hook for service compatibility.
        _logger.LogWarning("Broker operation failed.");
    }

    public void RegisterBrokerSuccess()
    {
        // Successful broker operations require no additional state transition.
    }
}
