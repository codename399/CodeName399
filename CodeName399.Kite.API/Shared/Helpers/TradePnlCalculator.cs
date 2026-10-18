using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;

namespace CodeName399.Kite.API.Helpers;

public static class TradePnlCalculator
{
    public static decimal Gross(TradeOrder trade, decimal exitPrice)
    {
        if (trade.Quantity <= 0 || trade.EntryPrice <= 0 || exitPrice <= 0)
            return 0m;

        return trade.Direction == TradeDirection.Short
            ? (trade.EntryPrice - exitPrice) * trade.Quantity
            : (exitPrice - trade.EntryPrice) * trade.Quantity;
    }

    public static decimal Net(TradeOrder trade, decimal exitPrice, decimal realizedCharges)
        => Gross(trade, exitPrice) - Math.Max(0m, realizedCharges);
}
