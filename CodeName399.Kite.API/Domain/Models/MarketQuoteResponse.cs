using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models
{
    public class MarketQuoteResponse
    {
        public bool Status { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
        public MarketQuoteContainer Data { get; set; } = new();
    }

    public class MarketQuoteContainer
    {
        public List<MarketQuoteData> Fetched { get; set; } = [];
    }

    // SmartAPI FULL quotes expose best bid/ask under depth.buy/depth.sell.
    // Keeping this separate from MarketQuoteData prevents JSON shape assumptions
    // from leaking into the trading model.
    public sealed class MarketQuoteDepthLevel
    {
        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("quantity")]
        public long Quantity { get; set; }

        [JsonPropertyName("orders")]
        public int Orders { get; set; }
    }

    public sealed class MarketQuoteDepth
    {
        [JsonPropertyName("buy")]
        public List<MarketQuoteDepthLevel> Buy { get; set; } = [];

        [JsonPropertyName("sell")]
        public List<MarketQuoteDepthLevel> Sell { get; set; } = [];
    }
}
