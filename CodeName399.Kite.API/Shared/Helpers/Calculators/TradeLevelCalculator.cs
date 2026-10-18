namespace CodeName399.Kite.API.Helpers.Calculators;

/// <summary>Calculates the protective stop used by the Core-only strategy.</summary>
public static class TradeLevelCalculator
{
    public static decimal CalculateProtectiveStop(
        decimal currentPrice,
        decimal atr,
        decimal atrStopMultiplier = 1.20m,
        decimal minimumStopPercent = 0.50m,
        decimal maximumStopPercent = 1.50m)
    {
        if (currentPrice <= 0m)
            return 0m;

        if (atr <= 0m)
            atr = currentPrice * (minimumStopPercent / 100m);

        var minimumRisk = currentPrice * (minimumStopPercent / 100m);
        var maximumRisk = currentPrice * (maximumStopPercent / 100m);
        var risk = Math.Clamp(atr * atrStopMultiplier, minimumRisk, maximumRisk);

        return Math.Round(currentPrice - risk, 2, MidpointRounding.AwayFromZero);
    }
}
