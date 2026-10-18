using System.Text.Json.Serialization;
namespace CodeName399.Kite.API.Models;

public class OrderBook
{
    [JsonPropertyName("orderid")] public string OrderId { get; set; } = "";
    [JsonPropertyName("tradingsymbol")] public string TradingSymbol { get; set; } = "";
    [JsonPropertyName("symboltoken")] public string SymbolToken { get; set; } = "";
    [JsonPropertyName("transactiontype")] public string TransactionType { get; set; } = "";
    [JsonPropertyName("orderstatus")] public string OrderStatus { get; set; } = "";
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
    [JsonPropertyName("filledshares")] public int FilledQuantity { get; set; }
    [JsonPropertyName("price")] public decimal Price { get; set; }
    [JsonPropertyName("averageprice")] public decimal AveragePrice { get; set; }
    [JsonPropertyName("exchange")] public string Exchange { get; set; } = "";
    [JsonPropertyName("producttype")] public string ProductType { get; set; } = "";
    [JsonPropertyName("ordertag")] public string OrderTag { get; set; } = "";
    [JsonPropertyName("updatetime")] public string UpdateTime { get; set; } = "";
}
