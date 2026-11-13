namespace CodeName399.Kite.API.Application.Services;

/// <summary>
/// Shared EMA implementation used by trading decisions, quote indicators and exports.
/// Seeds the EMA with the SMA of the first complete period, then applies alpha = 2 / (period + 1).
/// </summary>
public static class EmaCalculator
{
    public static decimal Calculate(IReadOnlyList<decimal> closingPrices, int period)
    {
        if (closingPrices == null || period <= 0 || closingPrices.Count < period)
            return 0m;

        var values = CalculateSeries(closingPrices, period);
        return values.Count == 0 ? 0m : values[^1];
    }

    public static List<decimal> CalculateSeries(IReadOnlyList<decimal> closingPrices, int period)
    {
        if (closingPrices == null)
            throw new ArgumentNullException(nameof(closingPrices));
        if (period <= 0)
            throw new ArgumentOutOfRangeException(nameof(period));

        var result = new List<decimal>(closingPrices.Count);
        if (closingPrices.Count == 0)
            return result;

        if (closingPrices.Count < period)
        {
            result.AddRange(Enumerable.Repeat(0m, closingPrices.Count));
            return result;
        }

        var ema = closingPrices.Take(period).Average();
        var alpha = 2m / (period + 1m);

        for (var i = 0; i < period - 1; i++)
            result.Add(0m);

        result.Add(ema);

        for (var i = period; i < closingPrices.Count; i++)
        {
            ema += (closingPrices[i] - ema) * alpha;
            result.Add(ema);
        }

        return result;
    }
}
