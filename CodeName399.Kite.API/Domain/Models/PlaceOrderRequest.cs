using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models
{
    public class PlaceOrderRequest
    {
        [JsonPropertyName("variety")]
        public string Variety { get; set; } = "NORMAL";

        [JsonPropertyName("tradingsymbol")]
        public string TradingSymbol { get; set; }

        [JsonPropertyName("symboltoken")]
        public string SymbolToken { get; set; }

        [JsonPropertyName("transactiontype")]
        public string TransactionType { get; set; }

        [JsonPropertyName("exchange")]
        public string Exchange { get; set; } = "NSE";

        [JsonPropertyName("ordertype")]
        public string OrderType { get; set; } = "MARKET";

        [JsonPropertyName("producttype")]
        public string ProductType { get; set; } = "INTRADAY";

        [JsonPropertyName("duration")]
        public string Duration { get; set; } = "DAY";

        [JsonPropertyName("quantity")]
        public int Quantity { get; set; }

        [JsonPropertyName("price")]
        public decimal Price { get; set; }

        [JsonPropertyName("triggerprice")]
        public decimal TriggerPrice { get; set; }

        [JsonPropertyName("ordertag")]
        public string OrderTag { get; set; } = "";

        // Required by SmartAPI for cash-market scrips under surveillance.
        // Set to "yes" only for configured NSE/BSE cash orders covered by SmartAPI surveillance consent.
        [JsonPropertyName("scripconsent")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ScripConsent { get; set; }

        [JsonIgnore]
        public bool IsExitOrder { get; set; }
    }
}