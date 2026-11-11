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
            var openingRange = dayCandles.Where(c => c.Timestamp.TimeOfDay >= new TimeSpan(9, 15, 0) && c.Timestamp.TimeOfDay < new TimeSpan(9, 20, 0)).ToList();
            if (openingRange.Count >= 3 && latest.Timestamp.TimeOfDay >= new TimeSpan(9, 20, 0) &&
                latest.Close > openingRange.Max(c => c.High) && latest.Close > latest.Open)
            { reason = "5-minute ORB breakout: completed candle closed above the 09:15-09:20 opening range high"; return true; }

            // EMA25 and VWAP are calculated once by CommonService.UpdateQuoteIndicators
            // and shared on the quote; do not recalculate them independently here.
            var ema25 = quote.Ema25;
            var previousEma25 = quote.PreviousCandleEma25;
            var vwap = quote.Vwap;
            if (ema25 > 0m && previousEma25 > 0m && vwap > 0m &&
                ema25 > previousEma25 && latest.Close > ema25 && latest.Close > vwap)
            { reason = $"EMA25 + VWAP bullish: close {latest.Close:F4} above rising EMA25 {ema25:F4} and VWAP {vwap:F4}"; return true; }
            return false;
        }

        private static decimal CalculateEma(IReadOnlyList<decimal> closes, int period)
        {
            if (closes == null || period <= 0 || closes.Count < period)
                return 0m;

            // SMA seed followed by the standard EMA smoothing recurrence.
            var ema = closes.Take(period).Average();
            var alpha = 2m / (period + 1m);

            for (var i = period; i < closes.Count; i++)
                ema = (closes[i] * alpha) + (ema * (1m - alpha));

            return Math.Round(ema, 6, MidpointRounding.AwayFromZero);
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


    }
}
