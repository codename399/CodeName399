namespace CodeName399.Kite.API.Models.Configuration;

public sealed class ExitSettings
{
    /// <summary>ATR distance used by trailing protection after charge-aware break-even.</summary>
    public decimal TrailingStopAtrMultiplier { get; set; } = 0.60m;

    /// <summary>Percentage of peak net profit retained by the trailing ratchet.</summary>
    public decimal TrailingProfitRetentionPercent { get; set; } = 70m;
}
