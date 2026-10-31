using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models
{
    public class MarketQuoteData
    {
        public string Exchange { get; set; } = string.Empty;
        public string TradingSymbol { get; set; } = string.Empty;
        public string SymbolToken { get; set; } = string.Empty;
        public string ProductType { get; set; } = "INTRADAY";
        public decimal Ltp { get; set; }
        public decimal Bid { get; set; }
        public decimal Ask { get; set; }
        public decimal Open { get; set; }
        public long LastTradedQuantity { get; set; }
        public decimal AveragePrice { get; set; }
        public long DayVolume { get; set; }
        public int Quantity { get; set; }
        public decimal PreviousClose { get; set; }
        public long Volume { get; set; }
        [JsonPropertyName("depth")]
        public MarketQuoteDepth? Depth { get; set; }
        public decimal Spread => Bid > 0 && Ask > 0 ? Math.Max(0, Ask - Bid) : 0;
        public decimal SpreadPercent => Ltp > 0 ? Spread / Ltp * 100m : 0;
        public decimal High { get; set; }
        public decimal Low { get; set; }
        public decimal Close { get; set; }
        public decimal PercentChange { get; set; }
        public decimal Ema9 { get; set; }
        public decimal Ema21 { get; set; }
        public decimal PreviousCandleEma9 { get; set; }
        public decimal PreviousCandleEma21 { get; set; }
        public decimal ChoppinessIndex { get; set; }
        public long TradeVolume { get; set; }
        public DateTime QuoteTimeUtc { get; set; }
        public decimal LowestPriceChange { get; set; }
        public decimal EstimatedCharges { get; set; }
        public decimal StopLoss { get; set; }

        public static TradeOrder ToTradeOrder(this MarketQuoteData quote)
        {
            return new TradeOrder
            {
                Symbol = quote.TradingSymbol,
                SymbolToken = quote.SymbolToken,
                Exchange = quote.Exchange,
                OrderType = "MARKET",
                Quantity = quote.Quantity, // Set the quantity as needed
                EntryPrice = quote.Ltp,
                StopLoss = quote.StopLoss, // Set the stop loss as needed
                EntryTime = DateTime.UtcNow,
                Ema9 = quote.Ema9,
                Ema21 = quote.Ema21,
                ChoppinessIndex = quote.ChoppinessIndex,
                ExitPrice = 0, // Set the exit price as needed
                ExitTime = null, // Set the exit time as needed
                EstimatedCharges = quote.EstimatedCharges,
                BreakEvenPrice = 0, // Set the break-even price as needed
                TrailingActivated = false // Set trailing activation as needed
            };
        }
    }
}
