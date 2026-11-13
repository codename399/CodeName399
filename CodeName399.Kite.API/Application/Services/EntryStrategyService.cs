using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Application.Services;

public sealed record EntryStrategyDiagnostics(
    int ValidCandlesLoaded,
    decimal? Ema9,
    decimal? Ema21,
    decimal? PreviousEma9,
    decimal? PreviousEma21,
    decimal? LastClose,
    DateTime? LastCandleTime,
    DateTime? PreviousCandleTime,
    bool EntryConditionSatisfied,
    string EntryRejectionReason);

/// <summary>Enters only on the completed candle where EMA9 crosses above EMA21.</summary>
public static class EntryStrategyService
{
    public static bool TryGetEntryReason(
        MarketQuoteData quote,
        List<Candle> candles,
        out string reason,
        out EntryStrategyDiagnostics diagnostics)
    {
        var ordered = (candles ?? [])
            .Where(c => c != null && c.Close > 0m)
            .GroupBy(c => c.Timestamp)
            .Select(g => g.Last())
            .OrderBy(c => c.Timestamp)
            .ToList();

        decimal? ema9 = null;
        decimal? ema21 = null;
        decimal? previousEma9 = null;
        decimal? previousEma21 = null;
        var crossed = false;

        if (ordered.Count >= 22)
        {
            var closes = ordered.Select(c => c.Close).ToList();
            var ema9Values = EmaCalculator.CalculateSeries(closes, 9);
            var ema21Values = EmaCalculator.CalculateSeries(closes, 21);

            ema9 = Math.Round(ema9Values[^1], 6, MidpointRounding.AwayFromZero);
            ema21 = Math.Round(ema21Values[^1], 6, MidpointRounding.AwayFromZero);
            previousEma9 = Math.Round(ema9Values[^2], 6, MidpointRounding.AwayFromZero);
            previousEma21 = Math.Round(ema21Values[^2], 6, MidpointRounding.AwayFromZero);

            // Compare unrounded values; rounding is for display only.
            crossed = ema9Values[^2] <= ema21Values[^2] &&
                      ema9Values[^1] > ema21Values[^1];
        }

        var rejection = ordered.Count < 22
            ? $"InsufficientValidCandles: required=22, loaded={ordered.Count}"
            : crossed ? string.Empty : "EMA9DidNotCrossAboveEMA21OnLatestClosedCandle";

        reason = crossed
            ? $"EMA9 crossed above EMA21 on candle close; EMA9={ema9:F6}, EMA21={ema21:F6}"
            : rejection;

        diagnostics = new EntryStrategyDiagnostics(
            ordered.Count,
            ema9,
            ema21,
            previousEma9,
            previousEma21,
            ordered.Count > 0 ? ordered[^1].Close : null,
            ordered.Count > 0 ? ordered[^1].Timestamp : null,
            ordered.Count > 1 ? ordered[^2].Timestamp : null,
            crossed,
            rejection);

        return crossed;
    }
}
