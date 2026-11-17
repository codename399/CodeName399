using CodeName399.Kite.API.Models;

public class MarketTick
{
    public string SymbolToken { get; set; } = "";
    public string TradingSymbol { get; set; } = "";
    public int ExchangeType { get; set; }
    public long SequenceNumber { get; set; }
    public DateTime ExchangeTime { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public int MarketDataMode { get; set; }

    public decimal Ltp { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal Spread => Bid > 0 && Ask > 0 ? Math.Max(0, Ask - Bid) : 0;
    public decimal SpreadPercent => Ltp > 0 ? Spread / Ltp * 100m : 0;

    public decimal Open { get; set; }
    public decimal High { get; set; }
    public decimal Low { get; set; }
    public decimal Close { get; set; }
    public long LastTradedQuantity { get; set; }
    public decimal AveragePrice { get; set; }
    public long DayVolume { get; set; }
    public double TotalBuyQuantity { get; set; }
    public double TotalSellQuantity { get; set; }
    public decimal PreviousClose { get; set; }
    public long Volume { get; set; }

    // Five-level equity order-book snapshot when supplied by the broker feed.
    public List<MarketDepthLevel> BidDepth { get; set; } = [];
    public List<MarketDepthLevel> AskDepth { get; set; } = [];
}
