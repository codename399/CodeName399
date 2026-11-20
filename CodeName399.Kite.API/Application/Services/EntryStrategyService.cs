using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Application.Services;

/// <summary>Entry is the lowest low of the latest ten completed one-minute candles.</summary>
public static class EntryStrategyService
{
    public static bool TryGetEntryReason(MarketQuoteData quote, List<Candle> candles, out decimal entryPrice, out string reason)
    {
        var completed = (candles ?? [])
            .Where(c => c != null && c.Low > 0m && c.Close > 0m)
            .GroupBy(c => c.Timestamp)
            .Select(g => g.Last())
            .OrderBy(c => c.Timestamp)
            .TakeLast(10)
            .ToList();

        entryPrice = 0m;
        if (completed.Count < 10)
        {
            reason = $"InsufficientCandles: required=10, loaded={completed.Count}";
            return false;
        }

        entryPrice = completed.Min(c => c.Low);
        var eligible = quote.Ltp > 0m && quote.Ltp == entryPrice;
        reason = eligible
            ? $"LTP matched lowest low of last 10 candles ({entryPrice:F2})"
            : $"WaitingForEntryPrice: LTP={quote.Ltp:F2}, EntryPrice={entryPrice:F2}";
        return eligible;
    }
}
