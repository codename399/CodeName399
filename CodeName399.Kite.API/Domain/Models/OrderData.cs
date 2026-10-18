using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models.WebSocketOrder;

public class OrderData
{
    [JsonPropertyName("variety")]
    public string Variety { get; set; } = "";

    [JsonPropertyName("ordertype")]
    public string OrderType { get; set; } = "";

    [JsonPropertyName("ordertag")]
    public string OrderTag { get; set; } = "";

    [JsonPropertyName("producttype")]
    public string ProductType { get; set; } = "";

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("triggerprice")]
    public decimal TriggerPrice { get; set; }

    [JsonPropertyName("quantity")]
    public string Quantity { get; set; } = "";

    [JsonPropertyName("disclosedquantity")]
    public string DisclosedQuantity { get; set; } = "";

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = "";

    [JsonPropertyName("squareoff")]
    public decimal SquareOff { get; set; }

    [JsonPropertyName("stoploss")]
    public decimal StopLoss { get; set; }

    [JsonPropertyName("trailingstoploss")]
    public decimal TrailingStopLoss { get; set; }

    [JsonPropertyName("tradingsymbol")]
    public string TradingSymbol { get; set; } = "";

    [JsonPropertyName("transactiontype")]
    public string TransactionType { get; set; } = "";

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = "";

    [JsonPropertyName("symboltoken")]
    public string SymbolToken { get; set; } = "";

    [JsonPropertyName("cancelsize")]
    public string CancelSize { get; set; } = "";

    [JsonPropertyName("averageprice")]
    public decimal AveragePrice { get; set; }

    [JsonPropertyName("filledshares")]
    public string FilledShares { get; set; } = "";

    [JsonPropertyName("unfilledshares")]
    public string UnfilledShares { get; set; } = "";

    [JsonPropertyName("orderid")]
    public string OrderId { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("orderstatus")]
    public string OrderStatus { get; set; } = "";

    [JsonPropertyName("updatetime")]
    public string UpdateTime { get; set; } = "";

    [JsonPropertyName("exchtime")]
    public string ExchangeTime { get; set; } = "";

    [JsonPropertyName("exchorderupdatetime")]
    public string ExchangeOrderUpdateTime { get; set; } = "";

    [JsonPropertyName("fillid")]
    public string FillId { get; set; } = "";

    [JsonPropertyName("filltime")]
    public string FillTime { get; set; } = "";

    [JsonPropertyName("parentorderid")]
    public string ParentOrderId { get; set; } = "";
}