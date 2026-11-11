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
        bool EntryConditionSatisfied,
        string EntryRejectionReason);

    /// <summary>
    /// Long entry when the latest completed 1-minute candle closes above both
    /// EMA(25) and session VWAP. Both indicators are rounded to six decimals.
    /// </summary>
    public static class EntryStrategyService
    {
        private const int EmaPeriod = 25;

        public static bool TryGetEntryReason(
            MarketQuoteData quote,
            List<Candle> candles,
            out string reason,
            out EntryStrategyDiagnostics diagnostics)
        { 
            var ordered = (candles ?? new List<Candle>())
                .Where(c => c != null && c.Open > 0m && c.Close > 0m && c.High > 0m &&
                            c.Low > 0m && c.High >= c.Low && c.Volume >= 0m)
                .GroupBy(c => c.Timestamp)
                .Select(g => g.Last())
                .OrderBy(c => c.Timestamp)
                .ToList();

            decimal? ema25 = null;
            decimal? vwap = null;
            decimal? close = ordered.Count > 0 ? ordered[^1].Close : null;
            var aboveEma = false;
            var aboveVwap = false;
            string rejection;

            if (ordered.Count < EmaPeriod)
            {
                rejection = $"InsufficientValidCandles: required={EmaPeriod}, loaded={ordered.Count}";
            }
            else
            {
                var closes = ordered.Select(c => c.Close).ToList();
                var ema = closes.Take(EmaPeriod).Average();
                var multiplier = 2m / (EmaPeriod + 1m);
                for (var i = EmaPeriod; i < closes.Count; i++)
                    ema = (closes[i] - ema) * multiplier + ema;
                ema25 = Math.Round(ema, 6, MidpointRounding.AwayFromZero);

                // VWAP resets each trading day; never mix volumes from prior sessions.
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
                    vwap = Math.Round(weighted / totalVolume, 6, MidpointRounding.AwayFromZero);
                    aboveEma = close!.Value > ema25.Value;
                    aboveVwap = close.Value > vwap.Value;
                    rejection = !aboveEma ? "CloseNotAboveEma25" :
                                !aboveVwap ? "CloseNotAboveVwap" : string.Empty;
                }
            }

            var success = rejection.Length == 0;
            reason = success
                ? $"EMA25 + VWAP: close {close!.Value:F6} > EMA25 {ema25!.Value:F6} and VWAP {vwap!.Value:F6}"
                : rejection;
            diagnostics = new EntryStrategyDiagnostics(ordered.Count, ema25, vwap, close,
                aboveEma, aboveVwap, success, rejection);
            return success;
        }
    }
}
