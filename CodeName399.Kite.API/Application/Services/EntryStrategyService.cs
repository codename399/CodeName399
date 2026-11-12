using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Application.Services
{
    public sealed record EntryStrategyDiagnostics(
        int ValidCandlesLoaded,
        decimal? Ema25,
        decimal? Vwap,
        decimal? LastClose,
        bool PriceAboveEma25,
        bool PriceAboveVwap,
        bool Ema25Rising,
        bool PullbackDetected,
        bool RecoveryConfirmed,
        decimal? PullbackSwingLow,
        decimal? SuggestedStopLoss,
        bool EntryConditionSatisfied,
        string EntryRejectionReason);

    /// <summary>
    /// Trend + pullback + recovery strategy for completed one-minute candles.
    /// A long entry requires an established EMA25/VWAP uptrend, a recent pullback
    /// towards either dynamic support, and a bullish recovery candle closing above
    /// the previous candle high, EMA25 and session VWAP.
    /// </summary>
    public static class EntryStrategyService
    {
        private const int EmaPeriod = 25;
        private const int TrendLookback = 3;
        private const int PullbackLookback = 5;
        private const decimal SupportProximityPercent = 0.30m;

        public static bool TryGetEntryReason(
            MarketQuoteData quote,
            List<Candle> candles,
            out string reason,
            out EntryStrategyDiagnostics diagnostics)
        {
            var ordered = Normalize(candles);
            decimal? ema25 = null;
            decimal? vwap = null;
            decimal? close = ordered.Count > 0 ? ordered[^1].Close : null;
            decimal? swingLow = null;
            decimal? suggestedStop = null;
            var aboveEma = false;
            var aboveVwap = false;
            var emaRising = false;
            var pullback = false;
            var recovery = false;
            string rejection;

            if (ordered.Count < EmaPeriod + TrendLookback + PullbackLookback + 1)
            {
                rejection = $"InsufficientValidCandles: required={EmaPeriod + TrendLookback + PullbackLookback + 1}, loaded={ordered.Count}";
            }
            else
            {
                var closes = ordered.Select(c => c.Close).ToList();
                var emaValues = CalculateEmaSeries(closes, EmaPeriod);
                var latestIndex = ordered.Count - 1;
                ema25 = Round6(emaValues[latestIndex]);
                emaRising = emaValues[latestIndex] > emaValues[latestIndex - TrendLookback];

                var sessionDate = ordered[^1].Timestamp.Date;
                var session = ordered.Where(c => c.Timestamp.Date == sessionDate && c.Volume > 0m).ToList();
                var totalVolume = session.Sum(c => c.Volume);
                if (totalVolume <= 0m)
                {
                    rejection = "SessionVwapUnavailable: no positive candle volume";
                }
                else
                {
                    var weighted = session.Sum(c => ((c.High + c.Low + c.Close) / 3m) * c.Volume);
                    vwap = Round6(weighted / totalVolume);

                    var latest = ordered[^1];
                    var previous = ordered[^2];
                    aboveEma = latest.Close > ema25.Value;
                    aboveVwap = latest.Close > vwap.Value;

                    var pullbackCandles = ordered
                        .Skip(Math.Max(0, ordered.Count - 1 - PullbackLookback))
                        .Take(PullbackLookback)
                        .Where(c => c.Timestamp.Date == sessionDate)
                        .ToList();

                    if (pullbackCandles.Count >= 3)
                    {
                        swingLow = pullbackCandles.Min(c => c.Low);
                        var support = Math.Max(ema25.Value, vwap.Value);
                        var proximity = support * SupportProximityPercent / 100m;
                        pullback = pullbackCandles.Any(c =>
                            Math.Abs(c.Low - ema25.Value) <= proximity ||
                            Math.Abs(c.Low - vwap.Value) <= proximity ||
                            (c.Low <= Math.Max(ema25.Value, vwap.Value) &&
                             c.High >= Math.Min(ema25.Value, vwap.Value)));
                    }

                    recovery = latest.Close > latest.Open &&
                               latest.Close > previous.High &&
                               latest.Close > ema25.Value &&
                               latest.Close > vwap.Value;

                    if (swingLow.HasValue)
                    {
                        // One paise below the observed pullback low; execution remains
                        // subject to the broker's instrument tick-size rounding.
                        suggestedStop = Math.Max(0m, swingLow.Value - 0.01m);
                    }

                    rejection = !emaRising ? "EMA25NotRising" :
                                !aboveVwap ? "CloseNotAboveVwap" :
                                !aboveEma ? "CloseNotAboveEma25" :
                                !pullback ? "NoRecentPullbackNearEma25OrVwap" :
                                !recovery ? "BullishRecoveryNotConfirmed" :
                                !suggestedStop.HasValue || suggestedStop.Value >= latest.Close
                                    ? "InvalidPullbackSwingLowStop" : string.Empty;
                }
            }

            var success = rejection.Length == 0;
            reason = success
                ? $"TrendPullbackRecovery: EMA25 rising; pullback swing low={swingLow:F4}; recovery close={close:F4} above previous high, EMA25={ema25:F6}, VWAP={vwap:F6}"
                : rejection;
            diagnostics = new EntryStrategyDiagnostics(
                ordered.Count, ema25, vwap, close, aboveEma, aboveVwap,
                emaRising, pullback, recovery, swingLow, suggestedStop,
                success, rejection);
            return success;
        }

        /// <summary>
        /// Candle-close exit signal: the latest completed candle closes below
        /// the current session VWAP. Protective stop and tick trailing exits remain
        /// higher priority and are handled by CommonService.
        /// </summary>
        public static bool TryGetVwapExitReason(List<Candle> candles, out decimal? vwap, out decimal? lastClose)
        {
            var ordered = Normalize(candles);
            vwap = null;
            lastClose = ordered.Count > 0 ? ordered[^1].Close : null;
            if (ordered.Count == 0) return false;

            var date = ordered[^1].Timestamp.Date;
            var session = ordered.Where(c => c.Timestamp.Date == date && c.Volume > 0m).ToList();
            var volume = session.Sum(c => c.Volume);
            if (volume <= 0m) return false;

            var weighted = session.Sum(c => ((c.High + c.Low + c.Close) / 3m) * c.Volume);
            vwap = Round6(weighted / volume);
            return lastClose.HasValue && lastClose.Value < vwap.Value;
        }

        private static List<Candle> Normalize(List<Candle> candles) =>
            (candles ?? new List<Candle>())
                .Where(c => c != null && c.Open > 0m && c.Close > 0m && c.High > 0m &&
                            c.Low > 0m && c.High >= c.Low && c.Volume >= 0m)
                .GroupBy(c => c.Timestamp)
                .Select(g => g.Last())
                .OrderBy(c => c.Timestamp)
                .ToList();

        private static List<decimal> CalculateEmaSeries(IReadOnlyList<decimal> closes, int period)
        {
            var values = new List<decimal>(closes.Count);
            if (closes.Count == 0) return values;
            var ema = closes.Take(Math.Min(period, closes.Count)).Average();
            var alpha = 2m / (period + 1m);
            for (var i = 0; i < closes.Count; i++)
            {
                if (i == period - 1)
                    values.Add(ema);
                else if (i >= period)
                {
                    ema = (closes[i] - ema) * alpha + ema;
                    values.Add(ema);
                }
                else
                    values.Add(0m);
            }
            return values;
        }

        private static decimal Round6(decimal value) =>
            Math.Round(value, 6, MidpointRounding.AwayFromZero);
    }
}
