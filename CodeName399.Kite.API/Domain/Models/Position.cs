using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models;

public class Position
{
    [JsonPropertyName("tradingsymbol")]
    public string TradingSymbol { get; set; } = string.Empty;

    [JsonPropertyName("symboltoken")]
    public string SymbolToken { get; set; } = string.Empty;

    [JsonPropertyName("exchange")]
    public string Exchange { get; set; } = string.Empty;

    [JsonPropertyName("producttype")]
    public string ProductType { get; set; } = string.Empty;

    [JsonPropertyName("netqty")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int NetQty { get; set; }

    [JsonPropertyName("buyqty")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int BuyQty { get; set; }

    [JsonPropertyName("sellqty")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public int SellQty { get; set; }

    [JsonPropertyName("buyavgprice")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal BuyAvgPrice { get; set; }

    [JsonPropertyName("sellavgprice")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal SellAvgPrice { get; set; }

    [JsonPropertyName("totalbuyavgprice")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal TotalBuyAvgPrice { get; set; }

    [JsonPropertyName("totalsellavgprice")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal TotalSellAvgPrice { get; set; }

    [JsonPropertyName("buyamount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal BuyAmount { get; set; }

    [JsonPropertyName("sellamount")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal SellAmount { get; set; }

    [JsonPropertyName("ltp")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Ltp { get; set; }

    [JsonPropertyName("close")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Close { get; set; }

    [JsonPropertyName("pnl")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Pnl { get; set; }

    [JsonPropertyName("realised")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Realised { get; set; }

    [JsonPropertyName("unrealised")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal Unrealised { get; set; }

    [JsonPropertyName("netvalue")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal NetValue { get; set; }

    [JsonPropertyName("netprice")]
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)]
    public decimal NetPrice { get; set; }
}
