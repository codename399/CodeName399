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
        public static bool ValidateBuyConditions(EntryStrategyEnum strategyEnum, MarketQuoteData quote, List<Candle> candles)
        {
            if (quote == null)
                return false;

            if (strategyEnum == EntryStrategyEnum.EmaCrossOver)
                return ValidateEmaCrossOver(quote);

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

            if (strategyEnum == EntryStrategyEnum.EmaCrossOver)
            {
                if (quote.Ema9 <= 0m || quote.Ema21 <= 0m ||
                    quote.PreviousCandleEma9 <= 0m || quote.PreviousCandleEma21 <= 0m)
                    return "EMADataUnavailable";

                if (quote.ChoppinessIndex > 61.8m)
                    return "MarketTooChoppy";

                if (quote.Ltp <= quote.Ema9 || quote.Ltp <= quote.Ema21)
                    return "LtpNotAboveBothEmas";

                if (quote.Ema9 <= quote.Ema21)
                    return "Ema9NotAboveEma21";

                if (quote.PreviousCandleEma9 > quote.PreviousCandleEma21)
                    return "CrossoverAlreadyOccurredBeforePreviousCandle";

                return "EmaCrossoverConditionsNotSatisfied";
            }

            if (candles == null || candles.Count < 3)
                return "InsufficientCandles";

            return "SelectedStrategyPatternNotSatisfied";
        }

        private static bool ValidateEmaCrossOver(MarketQuoteData quote)
        {
            if (quote.Ema9 <= 0 || quote.Ema21 <= 0 ||
                quote.PreviousCandleEma9 <= 0 || quote.PreviousCandleEma21 <= 0)
                return false;

            if (quote.ChoppinessIndex > 61.8m || quote.Ltp <= quote.Ema9 || quote.Ltp <= quote.Ema21)
                return false;

            return quote.Ema9 > quote.Ema21 &&
                   quote.PreviousCandleEma9 <= quote.PreviousCandleEma21;
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
