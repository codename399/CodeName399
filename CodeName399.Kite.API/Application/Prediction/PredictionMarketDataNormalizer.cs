using CodeName399.Kite.API.Models;

namespace CodeName399.Kite.API.Prediction;

/// <summary>
/// Repairs historical archives produced before the SmartAPI SNAP_QUOTE buy/sell
/// mapping was corrected. The old archive has Bid > Ask on almost every row.
/// Live ticks are produced by the corrected AngelBinaryParser and do not require
/// this compatibility normalization.
/// </summary>
public static class PredictionMarketDataNormalizer
{
    public static MarketTick NormalizeLegacyArchiveQuote(MarketTick tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        if (tick.Bid > 0m && tick.Ask > 0m && tick.Ask < tick.Bid)
        {
            (tick.Bid, tick.Ask) = (tick.Ask, tick.Bid);
            (tick.BidDepth, tick.AskDepth) = (tick.AskDepth, tick.BidDepth);
        }

        return tick;
    }
}
