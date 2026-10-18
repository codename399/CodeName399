using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Helpers.Calculators;

public static class IndicatorHelper
{
    public static IEnumerable<List<string>> ChunkTokens(List<string> tokens, int size = 50)
    {
        for (var i = 0; i < tokens.Count; i += size)
            yield return tokens.Skip(i).Take(size).ToList();
    }

    public static decimal CalculateATR(List<Candle> candles, int period = 14)
    {
        if (candles.Count <= period) return 0m;
        var trueRanges = new List<decimal>();
        for (var i = 1; i < candles.Count; i++)
        {
            var current = candles[i];
            var previous = candles[i - 1];
            trueRanges.Add(Math.Max(current.High - current.Low,
                Math.Max(Math.Abs(current.High - previous.Close), Math.Abs(current.Low - previous.Close))));
        }
        return trueRanges.TakeLast(period).Average();
    }
}
