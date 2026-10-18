namespace CodeName399.Kite.API.Models.Indicator;

/// <summary>Compact indicator and market-context snapshot captured for research.</summary>
public sealed class TradeIndicatorSnapshot
{
    public decimal ATR { get; set; }
    public decimal RecoveryScore { get; set; }
    public decimal BreakoutStrength { get; set; }
    public decimal PriceChangePercent { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal Spread { get; set; }
    public decimal SpreadPercent { get; set; }
    public decimal PreviousClose { get; set; }
    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long LastTradedQuantity { get; set; }
    public decimal AveragePrice { get; set; }
    public long DayVolume { get; set; }
    public double TotalBuyQuantity { get; set; }
    public double TotalSellQuantity { get; set; }
    public long Volume { get; set; }
    public bool HistoricalLoaded { get; set; }
    public DateTime LastHistoricalUpdate { get; set; }
    public string Signal { get; set; } = "";
    public string Reason { get; set; } = "";
}
