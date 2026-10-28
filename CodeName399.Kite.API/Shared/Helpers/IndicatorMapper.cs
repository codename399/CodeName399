using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Helpers;

/// <summary>Maps only Core/risk values into runtime and persisted trade snapshots.</summary>
public static class IndicatorMapper
{
    public static void ApplyIndicators(Gainer destination, IndicatorSnapshot source)
    {
        destination.ATR = source.ATR;
        destination.ChangePercent = source.ChangePercent;
        destination.CurrentPrice = source.CurrentPrice;
    }

    public static TradeIndicatorSnapshot ToTradeIndicatorSnapshot(Gainer source) => new()
    {
        ATR = source.ATR,
        RecoveryScore = source.RecoveryScore,
        BreakoutStrength = source.BreakoutStrength,
        PriceChangePercent = source.ChangePercent,
        CurrentPrice = source.CurrentPrice,
        Bid = source.Bid,
        Ask = source.Ask,
        Spread = source.Spread,
        SpreadPercent = source.SpreadPercent,
        PreviousClose = source.PreviousClose,
        Open = source.TickOpen,
        High = source.TickHigh,
        Low = source.TickLow,
        Close = source.TickClose,
        LastTradedQuantity = source.TickLastTradedQuantity,
        AveragePrice = source.TickAveragePrice,
        DayVolume = source.TickDayVolume,
        TotalBuyQuantity = source.TickTotalBuyQuantity,
        TotalSellQuantity = source.TickTotalSellQuantity,
        Volume = source.TickVolume,
        HistoricalLoaded = source.HistoricalLoaded,
        LastHistoricalUpdate = source.LastHistoricalUpdate,
        Signal = source.Signal,
        Reason = source.Reason
    };

    public static void ApplyTradeIndicatorSnapshot(Gainer destination, TradeIndicatorSnapshot source)
    {
        destination.ATR = source.ATR;
        destination.ChangePercent = source.PriceChangePercent;
        destination.CurrentPrice = source.CurrentPrice;
    }

    public static void ApplyTradeIndicators(TradeOrder trade, Gainer source, bool captureEntrySnapshot = false)
    {
        trade.ATR = source.ATR;
        if (captureEntrySnapshot || trade.EntryIndicators == null)
            trade.EntryIndicators = ToTradeIndicatorSnapshot(source);
    }
}
