namespace CodeName399.Kite.API.Models;

public class DashboardResponse
{
    public decimal AvailableCash { get; set; }
    public bool AutoTradingEnabled { get; set; }
    public bool PaperTrading { get; set; }
    public int DailyTrades { get; set; }
    public decimal DailyLoss { get; set; }
    public bool KillSwitch { get; set; }
    public string MarketStatus { get; set; } = "";
}
