namespace CodeName399.Kite.API.Models
{
    public class DailyTradeSummary
    {
        public string Symbol { get; set; } = "";

        public int Quantity { get; set; }

        public decimal EntryPrice { get; set; }

        public decimal ExitPrice { get; set; }

        public decimal Investment { get; set; }

        public decimal Charges { get; set; }

        public decimal GrossProfit { get; set; }

        public decimal ProfitLoss { get; set; }

        public decimal ProfitLossPercent { get; set; }

        public string ExitReason { get; set; } = "";

        public DateTime EntryTime { get; set; }

        public DateTime ExitTime { get; set; }

        public string TradeId { get; set; } = "";
        public string ExitCategory { get; set; } = "";
        public string ExitTrigger { get; set; } = "";
        public string EntryDecision { get; set; } = "";
        public string EntryReason { get; set; } = "";
        public bool IsOpen { get; set; }
    }
}