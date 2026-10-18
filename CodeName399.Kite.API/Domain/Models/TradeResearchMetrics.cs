using MongoDB.Bson.Serialization.Attributes;

namespace CodeName399.Kite.API.Models;

/// <summary>
/// Persisted path metrics collected during the unfiltered research phase.
/// These fields are descriptive only and do not make trading decisions.
/// </summary>
[BsonIgnoreExtraElements]
public sealed class TradeResearchMetrics
{
    public int TickCount { get; set; }
    public decimal PeakNetProfit { get; set; }
    public DateTime? PeakNetProfitTime { get; set; }
    public decimal MaxFavorablePriceMove { get; set; }
    public decimal MaxFavorableMovePercent { get; set; }
    public decimal MaxFavorableNetProfit { get; set; }
    public decimal MaxAdversePriceMove { get; set; }
    public decimal MaxAdverseMovePercent { get; set; }
    public decimal MaxAdverseNetProfit { get; set; }
    public DateTime? FirstPositiveNetProfitTime { get; set; }
    public decimal PriceAtFirstPositiveNetProfit { get; set; }
    public int PositiveNetProfitCrossings { get; set; }
    public double TimeInPositiveNetProfitSeconds { get; set; }
    public double TimeToPeakNetProfitSeconds { get; set; }
    public double TimeToMaxAdverseSeconds { get; set; }
    public decimal MaxDrawdownFromPeakNetProfit { get; set; }
    public decimal NetProfitAtExit { get; set; }
    public decimal GrossProfitAtExit { get; set; }
    public decimal RealizedChargesAtExit { get; set; }
    public double HoldingSeconds { get; set; }
    public decimal PeakPrice { get; set; }
    public decimal TroughPrice { get; set; }
    public bool PositiveNetProfitObserved { get; set; }
    public bool WasNetProfitableAtExit { get; set; }
    public string ResearchVersion { get; set; } = "v1";

    [BsonIgnore]
    public DateTime? LastPositiveStateTime { get; set; }
    [BsonIgnore]
    public bool LastWasPositive { get; set; }
}
