using CodeName399.Kite.API.Services.Brokerage;

namespace CodeName399.Kite.API.Models;

public class TradeValidationResult
{
    public bool CanTrade { get; set; }

    public string Reason { get; set; } = string.Empty;

    public BrokerageCharge Charges { get; set; } = new();

    public BrokerageCharge EntryCharges { get; set; } = new();

    public BrokerageCharge ExitCharges { get; set; } = new();

    public BrokerageCharge StopLossCharges { get; set; } = new();

    public decimal GrossProfit { get; set; }

    public decimal NetProfit { get; set; }

    public decimal ChargePercent { get; set; }

    public decimal ROI { get; set; }


    // NEW

    public decimal Investment { get; set; }

    public decimal Risk { get; set; }

    public decimal Reward { get; set; }

    public decimal RiskRewardRatio { get; set; }

    public decimal NetRisk { get; set; }

    public decimal NetReward { get; set; }

    public decimal NetRiskRewardRatio { get; set; }

    public string Suggestion { get; set; } = string.Empty;
}
