using CodeName399.Kite.API.Models.Enum;
using DatabaseRepository.Model;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeName399.Kite.API.Models
{
    [BsonIgnoreExtraElements]
    public class TradeOrder : Audit
    {
        public string Symbol { get; set; } = "";
        public string SymbolToken { get; set; } = "";
        public string Exchange { get; set; } = "NSE";
        public string OrderType { get; set; } = "MARKET";
        public int Quantity { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal StopLoss { get; set; }
        public decimal TotalInvestment { get; set; }
        public DateTime EntryTime { get; set; }
        public decimal Ema9 { get; set; }
        public decimal Ema21 { get; set; }
        public decimal ChoppinessIndex { get; set; }
        public decimal ExitPrice { get; set; }
        public DateTime? ExitTime { get; set; }
        public decimal EstimatedCharges { get; set; }
        public decimal BreakEvenPrice { get; set; }
        public bool TrailingActivated { get; set; }
        public string ExecutionMode { get; set; } = TradeExecutionMode.Paper.ToString();
        public decimal LowestPriceChange { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal NetProfit { get; set; }
    }
}
