namespace CodeName399.Kite.API.Models.Indicator;

public class CachedHistoricalData
{
    public string SymbolToken { get; set; } = string.Empty;
    public string Exchange { get; set; } = "NSE";

    public List<Candle> Candles1m { get; set; } = [];
    public List<Candle> Candles5m { get; set; } = [];

    public decimal CurrentPrice { get; set; }
    public long PreviousDayVolume { get; set; }
    public bool IsLiveVolumeInitialized { get; set; }

    public IndicatorSnapshot? Snapshot { get; set; }
    public bool IsReady { get; set; }

    public DateTime Last1MinuteRefresh { get; set; }
    public DateTime Last5MinuteRefresh { get; set; }
    public DateTime LastIndicatorUpdate { get; set; }
    public DateTime LastPriceUpdate { get; set; }

    public void UpdateSnapshot(IndicatorSnapshot snapshot)
    {
        Snapshot = snapshot;
        LastIndicatorUpdate = DateTime.UtcNow;
    }
}
