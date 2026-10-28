using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models;

public class ModifyOrderRequest
{
    [JsonPropertyName("orderid")]
    public string OrderId { get; set; } = string.Empty;

    [JsonPropertyName("variety")]
    public string Variety { get; set; } = "NORMAL";

    [JsonPropertyName("tradingsymbol")]
    public string TradingSymbol { get; set; } = string.Empty;

    [JsonPropertyName("symboltoken")]
    public string SymbolToken { get; set; } = string.Empty;

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = string.Empty;

    [JsonPropertyName("ordertype")]
    public string OrderType { get; set; } = "MARKET";

    [JsonPropertyName("producttype")]
    public string ProductType { get; set; } = "INTRADAY";

    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("triggerprice")]
    public decimal? TriggerPrice { get; set; }

    [JsonIgnore]
    public bool IsCasEligible { get; set; }
}
