using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Helpers.Calculators;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Services.Brokerage;

namespace CodeName399.Kite.API.Services;

public sealed class TradeValidationService
{
    private readonly IBrokerageCalculatorService _brokerage;
    private readonly ILogger<TradeValidationService> _logger;

    public TradeValidationService(
        IBrokerageCalculatorService brokerage,
        ILogger<TradeValidationService> logger)
    {
        _brokerage = brokerage;
        _logger = logger;
    }

    public async Task<TradeValidationResult> ValidateAsync(Gainer stock, int quantity)
    {
        using var flow = TradingFlowActivity.Start("trading.validation", stock.Symbol, stock.SymbolToken);
        var result = new TradeValidationResult();

        if (quantity <= 0)
            return Reject(result, "Invalid quantity.", "Quantity must be greater than zero.");
        if (stock.CurrentPrice <= 0m || stock.StopLoss <= 0m || stock.StopLoss >= stock.CurrentPrice)
            return Reject(result, "Invalid risk parameters.", "A valid price and protective stop are required for risk calculation.");

        var entryCharges = await _brokerage.CalculateAsync(
            stock.Symbol, stock.SymbolToken, stock.CurrentPrice, quantity,
            BrokerageTransactionType.Buy, stock.Exchange, stock.ProductType);
        var stopCharges = await _brokerage.CalculateAsync(
            stock.Symbol, stock.SymbolToken, stock.StopLoss, quantity,
            BrokerageTransactionType.Sell, stock.Exchange, stock.ProductType);

        result.EntryCharges = entryCharges;
        result.ExitCharges = stopCharges;
        result.StopLossCharges = stopCharges;
        result.Charges = BrokerageCharge.Combine(entryCharges, stopCharges);

        // Estimate the 1:2 reward level for reporting/validation only. This does
        // not add a fixed-target exit; the live exit remains stop/break-even/trailing.
        var riskPerShare = Math.Max(0m, stock.CurrentPrice - stock.StopLoss);
        result.Risk = riskPerShare * quantity;
        var estimatedTargetPrice = OrderPriceValidator.RoundUp(stock.CurrentPrice + (riskPerShare * 2m));
        var targetExitCharges = await _brokerage.CalculateAsync(
            stock.Symbol, stock.SymbolToken, estimatedTargetPrice, quantity,
            BrokerageTransactionType.Sell, stock.Exchange, stock.ProductType);
        var targetRoundTripCharges = BrokerageCharge.Combine(entryCharges, targetExitCharges);
        result.Reward = Math.Max(0m, estimatedTargetPrice - stock.CurrentPrice) * quantity;
        result.GrossProfit = result.Reward;
        result.NetProfit = result.GrossProfit - targetRoundTripCharges.TotalCharges;
        result.NetReward = result.NetProfit;
        result.NetRisk = result.Risk + result.Charges.TotalCharges;
        result.RiskRewardRatio = result.Risk > 0m ? result.Reward / result.Risk : 0m;
        result.NetRiskRewardRatio = result.NetRisk > 0m ? result.NetReward / result.NetRisk : 0m;
        result.Investment = stock.CurrentPrice * quantity;
        result.ROI = result.Investment > 0m ? result.NetProfit / result.Investment * 100m : 0m;
        var turnover = stock.CurrentPrice * quantity;
        result.ChargePercent = turnover > 0m ? Math.Round(result.Charges.TotalCharges / turnover * 100m, 2) : 0m;
        result.CanTrade = true;
        result.Reason = "Risk and charges validation passed.";

        _logger.LogDebug("Risk/charges validation passed. Symbol={Symbol}, Quantity={Quantity}, Risk={Risk:F2}, TargetGrossProfit={GrossProfit:F2}, TargetNetProfit={NetProfit:F2}, TargetNetRR={NetRiskRewardRatio:F3}, EntryCharges={EntryCharges:F2}, TargetExitCharges={TargetExitCharges:F2}",
            stock.Symbol, quantity, result.NetRisk, result.GrossProfit, result.NetProfit,
            result.NetRiskRewardRatio, entryCharges.TotalCharges, targetExitCharges.TotalCharges);
        return result;
    }

    private static TradeValidationResult Reject(TradeValidationResult result, string reason, string suggestion)
    {
        result.Reason = reason;
        result.Suggestion = suggestion;
        result.CanTrade = false;
        return result;
    }
}
