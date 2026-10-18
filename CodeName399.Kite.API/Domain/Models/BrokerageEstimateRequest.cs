using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models
{
    public class BrokerageEstimateRequest
    {
        [JsonPropertyName("orders")]
        public List<BrokerageEstimateOrder> Orders { get; set; } = [];
    }

    public class BrokerageEstimateOrder
    {
        [JsonPropertyName("product_type")]
        public string ProductType { get; set; } = "";

        [JsonPropertyName("transaction_type")]
        public string TransactionType { get; set; } = "";

        [JsonPropertyName("quantity")]
        public string Quantity { get; set; } = "";

        [JsonPropertyName("price")]
        public string Price { get; set; } = "";

        [JsonPropertyName("exchange")]
        public string Exchange { get; set; } = "NSE";

        [JsonPropertyName("symbol_name")]
        public string SymbolName { get; set; } = "";

        [JsonPropertyName("token")]
        public string Token { get; set; } = "";
    }
}
