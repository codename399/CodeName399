namespace CodeName399.Kite.API.Models
{
    public class TradeExecution
    {
        public int Quantity { get; set; }

        public decimal EntryPrice { get; set; }

        public string OrderId { get; set; } = "";

        public string UniqueOrderId { get; set; } = "";

        public decimal EstimatedCharges { get; set; }

        public decimal GrossProfit { get; set; }

        public decimal NetProfit { get; set; }

        public decimal ChargePercent { get; set; }
    }
}
