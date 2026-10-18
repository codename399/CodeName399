using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Models;

/// <summary>
/// In-memory execution candidate. This is not a active trade and is never
/// persisted. It carries only the state required while validating an entry.
/// </summary>
public sealed class TradeCandidate
{
    public Gainer Stock { get; set; } = default!;
    public string EntryDecision { get; set; } = "OPEN";
    public double EntryPredictionProbability { get; set; }
    public DateTime? EntryPredictionTimestampUtc { get; set; }
    public int PredictionModelVersion { get; set; }
    public TradeIndicatorSnapshot EntryIndicators { get; set; } = new();
    public string EligibilityReason { get; set; } = string.Empty;
    public string BuyEligibilityReason { get; set; } = string.Empty;
    public string? RejectReason { get; set; }

    // Snapshot captured when the candidate was created. Used to detect stale/adverse
    // price movement before the final broker order is submitted.
    public decimal SignalPrice { get; set; }
    public decimal SignalAtr { get; set; }
    public DateTime SignalTimeUtc { get; set; }

    // Execution telemetry captured while revalidating the candidate.
    public DateTime? ValidationTimeUtc { get; set; }
    public decimal ValidationPrice { get; set; }
    public double CandidateAgeSeconds { get; set; }
    public decimal AdverseMovePercentAtValidation { get; set; }
    public decimal AdverseMoveAtrAtValidation { get; set; }
    public bool? ValidationAccepted { get; set; }
    public string ValidationRejectReason { get; set; } = string.Empty;
}
