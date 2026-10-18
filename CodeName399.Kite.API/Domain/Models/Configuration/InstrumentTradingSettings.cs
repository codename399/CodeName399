using MongoDB.Bson.Serialization.Attributes;

namespace CodeName399.Kite.API.Models.Configuration;

[BsonIgnoreExtraElements]
public class InstrumentTradingSettings
{
    public string Exchange { get; set; } = "NSE";
    public string ProductType { get; set; } = "INTRADAY";
    public string OrderType { get; set; } = "MARKET";
    public string Duration { get; set; } = "DAY";
    public decimal AtrStopMultiplier { get; set; } = 1.2m;
    public decimal MinimumStopPercent { get; set; } = 0.5m;
    public decimal MaximumStopPercent { get; set; } = 1.0m;
    public int ExitOrderTimeoutSeconds { get; set; } = 10;
    public int MaximumExitRetries { get; set; } = 5;
    public int MaxMarketDataAgeSeconds { get; set; } = 15;
}
