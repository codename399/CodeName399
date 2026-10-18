namespace CodeName399.Kite.API.Models
{
    public class DailyTradingSummary
    {
        public DateOnly Date { get; set; }

        public decimal OpeningBalance { get; set; }

        public decimal ClosingBalance { get; set; }

        public decimal TotalInvestment { get; set; }

        public decimal GrossProfit { get; set; }

        public decimal GrossLoss { get; set; }

        public decimal TotalCharges { get; set; }

        public decimal TotalPnL { get; set; }

        public decimal Roi { get; set; }

        public int TotalTrades { get; set; }

        public int WinningTrades { get; set; }

        public int LosingTrades { get; set; }

        public decimal WinRate { get; set; }

        public DailyTradeSummary? BestTrade { get; set; }

        public DailyTradeSummary? WorstTrade { get; set; }

        public List<DailyTradeSummary> Trades { get; set; } = [];

        public List<DailyTradeSummary> OpenTrades { get; set; } = [];

        public int SafetyExits { get; set; }
        public decimal UnrealizedPnL { get; set; }
        public decimal NetPnlIncludingOpen { get; set; }
    }
}