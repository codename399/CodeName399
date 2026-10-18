using CodeName399.Kite.API.Models.Enum;

namespace CodeName399.Kite.API.Services
{
    public static class RiskManagementService
    {
        public static decimal CalculateCapitalForTrade(
            decimal availableBalance,
            decimal maxCapitalPerTradePercent,
            decimal riskMultiplier = 1m)
        {
            if (availableBalance <= 0m)
                return 0m;

            var percentage = Math.Clamp(maxCapitalPerTradePercent, 0m, 100m);
            var multiplier = Math.Clamp(riskMultiplier, 0.35m, 1.25m);

            return Math.Min(
                availableBalance,
                availableBalance * (percentage / 100m) * multiplier);
        }

        public static int CalculateQuantity(
            decimal capitalForTrade,
            decimal entryPrice,
            decimal stopLoss,
            decimal riskPercentage,
            TradeDirection direction = TradeDirection.Long,
            decimal maximumRiskAmount = 0m
        )
        {
            if (capitalForTrade <= 0 || entryPrice <= 0)
                return 0;

            decimal perShareRisk = direction == TradeDirection.Short
                ? stopLoss - entryPrice
                : entryPrice - stopLoss;

            if (perShareRisk <= 0)
                return 0;

            decimal riskAmount = capitalForTrade * (riskPercentage / 100m);
            if (maximumRiskAmount > 0m)
                riskAmount = Math.Min(riskAmount, maximumRiskAmount);
            int riskQty = (int)Math.Floor(riskAmount / perShareRisk);

            int capitalQty = (int)Math.Floor(capitalForTrade / entryPrice);
            int quantity = Math.Min(riskQty, capitalQty);

            return Math.Max(0, quantity);
        }
    }
}
