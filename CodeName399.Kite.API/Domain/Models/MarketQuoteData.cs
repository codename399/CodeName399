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
        public double TotalBuyQuantity { get; set; }
        public double TotalSellQuantity { get; set; }
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

        // Indicators calculated from completed one-minute historical candles.
        public decimal Ema9 { get; set; }
        public decimal Ema21 { get; set; }
        public decimal PreviousCandleEma9 { get; set; }
        public decimal PreviousCandleEma21 { get; set; }
        public decimal ChoppinessIndex { get; set; }

        public long TradeVolume { get; set; }
        public CodeName399.Kite.API.Models.Enum.InstrumentType InstrumentType { get; set; }

        [JsonPropertyName("upperCircuit")]
        public decimal UpperCircuitLimit { get; set; }

        [JsonPropertyName("lowerCircuit")]
        public decimal LowerCircuitLimit { get; set; }

        [JsonPropertyName("referenceLimitPrice")]
        public decimal ReferenceLimitPrice { get; set; }
        public DateTime QuoteTimeUtc { get; set; }
    }
}
