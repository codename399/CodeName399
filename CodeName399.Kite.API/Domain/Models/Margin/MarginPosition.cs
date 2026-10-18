using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models;

public class MarginPosition
{
    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = "NSE";

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("qty")]
    public int Qty { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("productType")]
    public string ProductType { get; set; } = "INTRADAY";

    [JsonPropertyName("tradeType")]
    public string TradeType { get; set; } = "BUY";

    [JsonPropertyName("orderType")]
    public string OrderType { get; set; } = "MARKET";
}
