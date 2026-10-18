using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Engine;

public sealed class IndicatorContext
{
    public required List<Candle> Candles1m { get; init; }
    public required List<Candle> Candles5m { get; init; }
    public required decimal CurrentPrice { get; init; }
    public decimal PreviousClose { get; init; }
    public string Symbol { get; init; } = "";
    public string SymbolToken { get; init; } = "";
}
