using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models
{
    public class BrokerageEstimateResponse
    {
        [JsonPropertyName("status")]
        public bool Status { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = "";

        [JsonPropertyName("data")]
        public BrokerageEstimateData Data { get; set; } = new();
    }

    public class BrokerageEstimateData
    {
        [JsonPropertyName("summary")]
        public BrokerageSummary Summary { get; set; } = new();
    }

    public class BrokerageSummary
    {
        [JsonPropertyName("trade_value")]
        public decimal TradeValue { get; set; }

        [JsonPropertyName("breakup")]
        public List<BrokerageBreakup> Breakup { get; set; } = [];
    }

    public class BrokerageBreakup
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("amount")]
        public decimal Amount { get; set; }

        [JsonPropertyName("breakup")]
        public List<BrokerageBreakup> BreakupDetails { get; set; } = [];
    }
}
