using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models.WebSocket;

public class AngelSubscriptionRequest
{
    [JsonPropertyName("correlationID")]
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("action")]
    public int Action { get; set; }

    [JsonPropertyName("params")]
    public AngelSubscriptionParams Params { get; set; } = new();
}

public class AngelSubscriptionParams
{
    [JsonPropertyName("mode")]
    public int Mode { get; set; }

    [JsonPropertyName("tokenList")]
    public List<AngelTokenSubscription> TokenList { get; set; } = [];
}

public class AngelTokenSubscription
{
    [JsonPropertyName("exchangeType")]
    public int ExchangeType { get; set; }

    [JsonPropertyName("tokens")]
    public List<string> Tokens { get; set; } = [];
}
