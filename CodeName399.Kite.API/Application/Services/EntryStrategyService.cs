using CodeName399.Kite.API.Domain.Models.Enum;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Application.Services
{
    /// <summary>
    /// Candle-based long-entry validation for the selectable entry strategies.
    /// The supplied candle collection is expected to be chronological (oldest first).
    /// </summary>
    public static class EntryStrategyService
    {
        public static bool TryGetEntryReason(MarketQuoteData quote, List<Candle> candles, out string reason)
        {
            reason = string.Empty;
            if (quote == null || candles == null) return false;
            var ordered = candles.Where(c => c != null && c.Close > 0m && c.High >= c.Low)
                .GroupBy(c => c.Timestamp).Select(g => g.Last()).OrderBy(c => c.Timestamp).ToList();
            if (ordered.Count < 26) return false;

            // Independent entry paths: any one valid setup may trigger a BUY.
            if (ValidateEmaCrossOverAtCandleClose(ordered))
            { reason = "EMA9 crossed above EMA21 on the completed 1-minute candle"; return true; }

            var latest = ordered[^1];
            var dayCandles = ordered.Where(c => c.Timestamp.Date == latest.Timestamp.Date).ToList();
            var openingRange = dayCandles.Where(c => c.Timestamp.TimeOfDay >= new TimeSpan(9,15,0) && c.Timestamp.TimeOfDay < new TimeSpan(9,20,0)).ToList();
            if (openingRange.Count >= 3 && latest.Timestamp.TimeOfDay >= new TimeSpan(9,20,0) &&
                latest.Close > openingRange.Max(c => c.High) && latest.Close > latest.Open)
            { reason = "5-minute ORB breakout: completed candle closed above the 09:15-09:20 opening range high"; return true; }

            var closes = ordered.Select(c => c.Close).ToList();
            var ema25 = CalculateEma(closes, 25);
            var previousEma25 = CalculateEma(closes.Take(closes.Count - 1).ToList(), 25);
            var volumeCandles = dayCandles.Where(c => c.Volume > 0).ToList();
            var volume = volumeCandles.Sum(c => c.Volume);
            var vwap = volume > 0 ? volumeCandles.Sum(c => ((c.High + c.Low + c.Close) / 3m) * c.Volume) / volume : 0m;
            if (vwap > 0 && ema25 > previousEma25 && latest.Close > ema25 && latest.Close > vwap)
            { reason = $"EMA25 + VWAP bullish: close {latest.Close:F4} above rising EMA25 {ema25:F4} and VWAP {vwap:F4}"; return true; }
            return false;
        }

        public static bool ValidateBuyConditions(EntryStrategyEnum strategyEnum, MarketQuoteData quote, List<Candle> candles)
        {
            if (TryGetEntryReason(quote, candles, out _)) return true;
            if (quote == null)
                return false;

            // Entry is triggered by the close of the completed candle that first
            // moves EMA9 above EMA21. Treat the legacy pullback strategy setting
            // the same way so persisted configuration cannot keep delaying entries.
            if (strategyEnum is EntryStrategyEnum.EmaCrossOver or EntryStrategyEnum.EmaCrossOverWithPullback)
                return ValidateEmaCrossOverAtCandleClose(candles);

            if (candles == null || candles.Count < 3)
                return false;

            var completedCandles = candles
                .Where(c => c != null && c.High >= c.Low && c.Close > 0)
                .OrderBy(c => c.Timestamp)
                .ToList();

            if (completedCandles.Count < 3)
                return false;

            return strategyEnum switch
            {
                EntryStrategyEnum.EmaCrossOverWithPullback => ValidateEmaCrossOverWithPullback(quote, completedCandles),
                EntryStrategyEnum.FVGWithSweep => ValidateFvgWithSweep(quote, completedCandles),
                EntryStrategyEnum.NoiseBand => ValidateNoiseBandBreakout(quote, completedCandles),
                _ => false
            };
        }

        /// <summary>
        /// Returns a human-readable reason when an entry strategy rejects a quote.
        /// The result is diagnostic only; it does not change the strategy decision.
        /// </summary>
        public static string GetBuyRejectionReason(EntryStrategyEnum strategyEnum, MarketQuoteData quote, List<Candle> candles)
        {
            if (quote == null)
                return "QuoteUnavailable";

            if (strategyEnum is EntryStrategyEnum.EmaCrossOver or EntryStrategyEnum.EmaCrossOverWithPullback)
            {
                if (candles == null || candles.Count < 22)
                    return "InsufficientCandlesForCrossover";
                var ordered = candles.Where(c => c != null && c.Close > 0 && c.High >= c.Low)
                    .GroupBy(c => c.Timestamp).Select(g => g.Last()).OrderBy(c => c.Timestamp).ToList();
                if (ordered.Count < 22) return "InsufficientCompletedCandlesForCrossover";
                var closes = ordered.Select(c => c.Close).ToList();
                var current9 = CalculateEma(closes, 9);
                var current21 = CalculateEma(closes, 21);
                var previousCloses = closes.Take(closes.Count - 1).ToList();
                var previous9 = CalculateEma(previousCloses, 9);
                var previous21 = CalculateEma(previousCloses, 21);
                // A BUY is eligible only on the transition from EMA9 at/below EMA21
                // on the prior completed candle to EMA9 strictly above EMA21 now.
                // Merely remaining above EMA21 is NOT a new entry signal.
                var crossedAbove = previous9 <= previous21 && current9 > current21;
                return crossedAbove
                    ? "BullishEma9CrossAboveEma21OnLatestClosedCandle"
                    : "NoFreshBullishEma9CrossAboveEma21";
            }

            if (candles == null || candles.Count < 3)
                return "InsufficientCandles";

            return "SelectedStrategyPatternNotSatisfied";
        }

        private static bool ValidateEmaCrossOverAtCandleClose(List<Candle> candles)
        {
            if (candles == null) return false;

            var completed = candles
                .Where(c => c != null && c.Close > 0m && c.High >= c.Low)
                .GroupBy(c => c.Timestamp)
                .Select(g => g.Last())
                .OrderBy(c => c.Timestamp)
                .ToList();

            // Need at least 21 observations before the previous candle and one
            // completed candle on which to detect the crossover.
            if (completed.Count < 22) return false;

            var closes = completed.Select(c => c.Close).ToList();
            var previousCloses = closes.Take(closes.Count - 1).ToList();
            var currentEma9 = CalculateEma(closes, 9);
            var currentEma21 = CalculateEma(closes, 21);
            var previousEma9 = CalculateEma(previousCloses, 9);
            var previousEma21 = CalculateEma(previousCloses, 21);

            // The ONLY BUY signal is a fresh bullish crossover on the latest
            // completed candle:
            //   previous candle: EMA9 <= EMA21
            //   latest candle:   EMA9 > EMA21
            // If EMA9 was already above EMA21 before this candle, do not enter.
            // No pullback/recovery or other entry pattern can create a BUY.
            var wasAtOrBelow = previousEma9 <= previousEma21;
            var isNowStrictlyAbove = currentEma9 > currentEma21;
            return wasAtOrBelow && isNowStrictlyAbove;
        }

        private static decimal CalculateEma(IReadOnlyList<decimal> closes, int period)
        {
            if (closes == null || closes.Count < period) return 0m;
            var ema = closes.Take(period).Average();
            var alpha = 2m / (period + 1m);
            for (var i = period; i < closes.Count; i++)
                ema = closes[i] * alpha + ema * (1m - alpha);
            return ema;
        }

        /// <summary>
        /// EMA trend entry from the video: range break, decisive close above both
        /// averages, widening EMA spread, then a retest that holds the breakout.
        /// </summary>
        private static bool ValidateEmaCrossOverWithPullback(MarketQuoteData quote, List<Candle> candles)
        {
            if (candles.Count < 12 ||
                quote.Ema9 <= 0 || quote.Ema21 <= 0 ||
                quote.PreviousCandleEma9 <= 0 || quote.PreviousCandleEma21 <= 0 ||
                quote.Ema9 <= quote.Ema21 || quote.Ltp <= quote.Ema9 || quote.Ltp <= quote.Ema21 ||
                quote.ChoppinessIndex > 61.8m)
                return false;

            var latest = candles[^1];
            var previous = candles[^2];
            var rangeStart = candles.Count - 12;
            var priorRange = candles.Skip(rangeStart).Take(9).ToList();
            var breakoutLevel = priorRange.Max(c => c.High);

            // The breakout candle must close above the prior range and both EMAs.
            var breakout = candles[^3];
            var breakoutRange = breakout.High - breakout.Low;
            var breakoutBody = breakout.Close - breakout.Open;
            if (breakoutRange <= 0 || breakout.Close <= breakoutLevel ||
                breakout.Close <= quote.Ema9 || breakout.Close <= quote.Ema21 ||
                breakoutBody <= 0 || breakoutBody / breakoutRange < 0.60m)
                return false;

            var currentSpread = quote.Ema9 - quote.Ema21;
            var previousSpread = quote.PreviousCandleEma9 - quote.PreviousCandleEma21;
            if (currentSpread <= previousSpread || currentSpread <= 0)
                return false;

            // A pullback must revisit the broken level without closing back below it.
            var retested = latest.Low <= breakoutLevel * 1.002m &&
                           latest.Low >= breakoutLevel * 0.995m;
            var held = latest.Close >= breakoutLevel && previous.Close >= breakoutLevel * 0.995m;
            return retested && held && quote.Ltp >= breakoutLevel;
        }

        /// <summary>
        /// Bullish FVG entry: a prior liquidity sweep, bullish displacement that
        /// breaks structure and creates a three-candle gap, then its first 50% retest.
        /// </summary>
        private static bool ValidateFvgWithSweep(MarketQuoteData quote, List<Candle> candles)
        {
            if (candles.Count < 8 || quote.Ltp <= 0)
                return false;

            var latestIndex = candles.Count - 1;
            var firstPossiblePattern = Math.Max(3, candles.Count - 12);

            for (var thirdIndex = latestIndex - 1; thirdIndex >= firstPossiblePattern + 2; thirdIndex--)
            {
                var first = candles[thirdIndex - 2];
                var displacement = candles[thirdIndex - 1];
                var third = candles[thirdIndex];

                // Bullish fair-value gap between candle one's high and candle
                // three's low, created by a strong bullish displacement candle.
                var gapLow = first.High;
                var gapHigh = third.Low;
                if (gapHigh <= gapLow || displacement.Close <= displacement.Open)
                    continue;

                var displacementRange = displacement.High - displacement.Low;
                if (displacementRange <= 0 ||
                    (displacement.Close - displacement.Open) / displacementRange < 0.65m)
                    continue;

                var structureStart = Math.Max(0, thirdIndex - 8);
                var structureHigh = candles.Skip(structureStart).Take(thirdIndex - structureStart - 2)
                    .Select(c => c.High).DefaultIfEmpty(0).Max();
                if (structureHigh <= 0 || displacement.Close <= structureHigh)
                    continue;

                // The candle immediately before the setup must sweep a recent low
                // and recover above it before displacement.
                var sweepIndex = thirdIndex - 3;
                if (sweepIndex < 1)
                    continue;
                var sweep = candles[sweepIndex];
                var priorLow = candles.Skip(Math.Max(0, sweepIndex - 5)).Take(Math.Min(5, sweepIndex))
                    .Select(c => c.Low).DefaultIfEmpty(decimal.MaxValue).Min();
                if (priorLow == decimal.MaxValue || sweep.Low >= priorLow || sweep.Close <= priorLow)
                    continue;

                // Only the first revisit is tradable. Any intervening candle touching
                // the gap invalidates this setup.
                var touchedBeforeLatest = candles.Skip(thirdIndex + 1)
                    .Take(latestIndex - thirdIndex - 1)
                    .Any(c => c.Low <= gapHigh && c.High >= gapLow);
                if (touchedBeforeLatest)
                    continue;

                var midpoint = (gapLow + gapHigh) / 2m;
                var current = candles[latestIndex];
                var isFirstTouch = current.Low <= midpoint && current.High >= midpoint;
                var priceInGap = quote.Ltp >= gapLow && quote.Ltp <= midpoint;
                if (isFirstTouch && priceInGap)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Volatility-compression entry: wait for several narrow candles, then
        /// require a strong bullish close beyond the compression range.
        /// </summary>
        private static bool ValidateNoiseBandBreakout(MarketQuoteData quote, List<Candle> candles)
        {
            if (candles.Count < 10 || quote.Ltp <= 0)
                return false;

            var latest = candles[^1];
            var baseline = candles.Skip(candles.Count - 10).Take(4).ToList();
            var compression = candles.Skip(candles.Count - 6).Take(5).ToList();

            var compressionRange = compression.Average(c => c.High - c.Low);
            var baselineRange = baseline.Average(c => c.High - c.Low);
            if (baselineRange <= 0 || compressionRange > baselineRange * 0.70m)
                return false;

            var compressionHigh = compression.Max(c => c.High);
            var compressionLow = compression.Min(c => c.Low);
            var latestRange = latest.High - latest.Low;
            if (latestRange <= 0 || latest.Close <= latest.Open)
                return false;

            var bullishBodyRatio = (latest.Close - latest.Open) / latestRange;
            var brokeOut = latest.Close > compressionHigh && quote.Ltp >= latest.Close;
            return brokeOut && bullishBodyRatio >= 0.65m &&
                   latest.Close > compressionLow &&
                   (quote.Ema9 <= 0 || quote.Ema21 <= 0 || quote.Ema9 >= quote.Ema21);
        }
    }
}
