using MongoDB.Bson.Serialization.Attributes;

namespace CodeName399.Kite.API.Models;

/// <summary>
/// Persisted execution telemetry for an entry order. This records the timing
/// between signal, validation, broker submission/acknowledgement and fill so
/// entry quality can be separated from execution quality without guessing.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class EntryExecutionAudit
{
    public DateTime SignalTimeUtc { get; set; }
    public decimal SignalPrice { get; set; }
    public decimal SignalAtr { get; set; }

    public DateTime? ValidationTimeUtc { get; set; }
    public decimal ValidationPrice { get; set; }
    public double CandidateAgeSeconds { get; set; }
    public decimal AdverseMovePercentAtValidation { get; set; }
    public decimal AdverseMoveAtrAtValidation { get; set; }
    public bool? ValidationAccepted { get; set; }
    public string ValidationRejectReason { get; set; } = string.Empty;

    public DateTime? OrderSubmitTimeUtc { get; set; }
    public DateTime? BrokerResponseTimeUtc { get; set; }
    public bool? BrokerAccepted { get; set; }
    public string BrokerResponseMessage { get; set; } = string.Empty;
    public string BrokerOrderId { get; set; } = string.Empty;
    public string BrokerUniqueOrderId { get; set; } = string.Empty;

    public DateTime? BrokerAckTimeUtc { get; set; }
    public DateTime? FirstFillTimeUtc { get; set; }
    public decimal FillPrice { get; set; }
    public int FillQuantity { get; set; }

    /// <summary>Fill versus signal price. Positive means favorable for the trade direction.</summary>
    public decimal FillSlippagePercent { get; set; }

    /// <summary>Time from order submission to the first broker acknowledgement.</summary>
    public double? SubmitToAckMilliseconds { get; set; }

    /// <summary>Time from order submission to first fill.</summary>
    public double? SubmitToFillMilliseconds { get; set; }

    /// <summary>Time from signal creation to first fill.</summary>
    public double? SignalToFillMilliseconds { get; set; }

    public string RejectionReason { get; set; } = string.Empty;

    /// <summary>First N market ticks after the actual entry fill.</summary>
    public List<EntryExecutionTick> PostFillTicks { get; set; } = new();
}

/// <summary>Compact post-fill tick snapshot retained by the entry execution audit.</summary>
[BsonIgnoreExtraElements]
public sealed class EntryExecutionTick
{
    public int Sequence { get; set; }
    public DateTime TimestampUtc { get; set; }
    public decimal Price { get; set; }
    public decimal PreviousPrice { get; set; }
    public decimal MoveFromFillPercent { get; set; }
    public decimal MoveFromPreviousPercent { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal Spread { get; set; }
    public long Volume { get; set; }
    public decimal MovementScore { get; set; }
    public decimal TrendStrength { get; set; }
    public decimal TrendStability { get; set; }
    public decimal RecoveryScore { get; set; }
    public decimal BreakoutStrength { get; set; }
    public decimal NoiseScore { get; set; }
    public decimal TickQualityScore { get; set; }
    public decimal Confidence { get; set; }
}
