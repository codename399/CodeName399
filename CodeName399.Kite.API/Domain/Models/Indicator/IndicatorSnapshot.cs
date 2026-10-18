namespace CodeName399.Kite.API.Models.Indicator;

public sealed class IndicatorSnapshot
{
    public required List<Candle> Candles1m { get; init; }
    public required List<Candle> Candles5m { get; init; }
    public decimal CurrentPrice { get; init; }
    public decimal PreviousClose { get; init; }
    public decimal ChangePercent { get; init; }
    public decimal ATR { get; init; }
}
