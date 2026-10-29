using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;

namespace CodeName399.Kite.API.Domain.Models;

/// <summary>
/// Minimal durable state required to recover an active trade after process restart.
/// Analytics, research snapshots and closed-trade data are intentionally excluded.
/// </summary>
public sealed class TradeOrderRecoveryRecord
{
    public string TradeId { get; set; } = "";
    public string Symbol { get; set; } = "";
    public string SymbolToken { get; set; } = "";
    public InstrumentType InstrumentType { get; set; } = InstrumentType.Equity;
    public TradeDirection Direction { get; set; } = TradeDirection.Long;
    public TradeExecutionMode ExecutionMode { get; set; } = TradeExecutionMode.Paper;
    public string EntryTransactionType { get; set; } = "BUY";
    public string ExitTransactionType { get; set; } = "SELL";
    public string Exchange { get; set; } = "NSE";
    public string ProductType { get; set; } = "INTRADAY";
    public string OrderType { get; set; } = "MARKET";
    public string Duration { get; set; } = "DAY";

    public int Quantity { get; set; }
    public decimal EntryPrice { get; set; }
    public decimal StopLoss { get; set; }
    public decimal InitialStopLoss { get; set; }
    public decimal InitialRiskPerShare { get; set; }
    public decimal HighestPrice { get; set; }
    public decimal LowestPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal ATR { get; set; }
    public DateTime EntryTime { get; set; }

    public string BuyOrderId { get; set; } = "";
    public string StopLossOrderId { get; set; } = "";
    public string UniqueOrderId { get; set; } = "";
    public string ExitOrderId { get; set; } = "";
    public string ExitOrderTag { get; set; } = "";
    public string ExitUniqueOrderId { get; set; } = "";

    public decimal BreakEvenPrice { get; set; }
    public decimal MinimumExitPrice { get; set; }
    public decimal ReservedCapital { get; set; }
    public decimal EstimatedCharges { get; set; }
    public decimal EstimatedEntryCharges { get; set; }
    public decimal EstimatedExitCharges { get; set; }

    public bool Closed { get; set; }
    public bool ExitOrderPlaced { get; set; }
    public DateTime? ExitOrderPlacedAt { get; set; }
    public DateTime? ExitLastAttemptTime { get; set; }
    public int ExitRetryCount { get; set; }
    public bool ExitRequired { get; set; }
    public string ExitReason { get; set; } = "";
    public decimal ExitPrice { get; set; }
    public DateTime? ExitTime { get; set; }
    public string ExitTrigger { get; set; } = "";
    public decimal ExitTriggerPrice { get; set; }

    public int EntryRequestedQuantity { get; set; }
    public int EntryFilledQuantity { get; set; }
    public decimal EntryAveragePrice { get; set; }
    public OrderExecutionState EntryExecutionState { get; set; } = OrderExecutionState.Submitted;
    public DateTime? EntryLastUpdateTime { get; set; }
    public int ExitRequestedQuantity { get; set; }
    public int ExitFilledQuantity { get; set; }
    public decimal ExitAveragePrice { get; set; }
    public OrderExecutionState ExitExecutionState { get; set; } = OrderExecutionState.Submitted;
    public DateTime? ExitLastUpdateTime { get; set; }
    public TradeStatus Status { get; set; } = TradeStatus.PendingBrokerConfirmation;

    public bool StopLossTriggered { get; set; }
    public decimal StopLossTriggerPrice { get; set; }
    public DateTime? StopLossTriggerTime { get; set; }
    public bool TrailingActivated { get; set; }
    public DateTime? TrailingActivatedTime { get; set; }
    public int TrailingMoveCount { get; set; }
    public decimal TrailingProtectedProfit { get; set; }
    public decimal PeakEstimatedNetProfit { get; set; }

    public string EligibilityReason { get; set; } = "";
    public string EntryReason { get; set; } = "";
    public string EntryDecision { get; set; } = "";
    public string BuyEligibilityReason { get; set; } = "";
    public string? RejectReason { get; set; }

    public static TradeOrderRecoveryRecord From(TradeOrder trade) => new()
    {
        TradeId = string.IsNullOrWhiteSpace(trade.Id) ? Guid.NewGuid().ToString("N") : trade.Id,
        Symbol = trade.Symbol,
        SymbolToken = trade.SymbolToken,
        InstrumentType = trade.InstrumentType,
        Direction = trade.Direction,
        ExecutionMode = trade.ExecutionMode,
        EntryTransactionType = trade.EntryTransactionType,
        ExitTransactionType = trade.ExitTransactionType,
        Exchange = trade.Exchange,
        ProductType = trade.ProductType,
        OrderType = trade.OrderType,
        Duration = trade.Duration,
        Quantity = trade.Quantity,
        EntryPrice = trade.EntryPrice,
        StopLoss = trade.StopLoss,
        InitialStopLoss = trade.InitialStopLoss,
        InitialRiskPerShare = trade.InitialRiskPerShare,
        HighestPrice = trade.HighestPrice,
        LowestPrice = trade.LowestPrice,
        CurrentPrice = trade.CurrentPrice,
        ATR = trade.ATR,
        EntryTime = trade.EntryTime,
        BuyOrderId = trade.BuyOrderId,
        StopLossOrderId = trade.StopLossOrderId,
        UniqueOrderId = trade.UniqueOrderId,
        ExitOrderId = trade.ExitOrderId,
        ExitOrderTag = trade.ExitOrderTag,
        ExitUniqueOrderId = trade.ExitUniqueOrderId,
        BreakEvenPrice = trade.BreakEvenPrice,
        MinimumExitPrice = trade.MinimumExitPrice,
        ReservedCapital = trade.ReservedCapital,
        EstimatedCharges = trade.EstimatedCharges,
        EstimatedEntryCharges = trade.EstimatedEntryCharges,
        EstimatedExitCharges = trade.EstimatedExitCharges,
        Closed = trade.Closed,
        ExitOrderPlaced = trade.ExitOrderPlaced,
        ExitOrderPlacedAt = trade.ExitOrderPlacedAt,
        ExitLastAttemptTime = trade.ExitLastAttemptTime,
        ExitRetryCount = trade.ExitRetryCount,
        ExitRequired = trade.ExitRequired,
        ExitReason = trade.ExitReason,
        ExitPrice = trade.ExitPrice,
        ExitTime = trade.ExitTime,
        ExitTrigger = trade.ExitTrigger,
        ExitTriggerPrice = trade.ExitTriggerPrice,
        EntryRequestedQuantity = trade.EntryRequestedQuantity,
        EntryFilledQuantity = trade.EntryFilledQuantity,
        EntryAveragePrice = trade.EntryAveragePrice,
        EntryExecutionState = trade.EntryExecutionState,
        EntryLastUpdateTime = trade.EntryLastUpdateTime,
        ExitRequestedQuantity = trade.ExitRequestedQuantity,
        ExitFilledQuantity = trade.ExitFilledQuantity,
        ExitAveragePrice = trade.ExitAveragePrice,
        ExitExecutionState = trade.ExitExecutionState,
        ExitLastUpdateTime = trade.ExitLastUpdateTime,
        Status = trade.Status,
        StopLossTriggered = trade.StopLossTriggered,
        StopLossTriggerPrice = trade.StopLossTriggerPrice,
        StopLossTriggerTime = trade.StopLossTriggerTime,
        TrailingActivated = trade.TrailingActivated,
        TrailingActivatedTime = trade.TrailingActivatedTime,
        TrailingMoveCount = trade.TrailingMoveCount,
        TrailingProtectedProfit = trade.TrailingProtectedProfit,
        PeakEstimatedNetProfit = trade.PeakEstimatedNetProfit,
        EligibilityReason = trade.EligibilityReason,
        EntryReason = trade.EntryReason,
        EntryDecision = trade.EntryDecision,
        BuyEligibilityReason = trade.BuyEligibilityReason,
        RejectReason = trade.RejectReason
    };

    public TradeOrder ToTradeOrder() => new()
    {
        Id = TradeId,
        Symbol = Symbol,
        SymbolToken = SymbolToken,
        InstrumentType = InstrumentType,
        Direction = Direction,
        ExecutionMode = ExecutionMode,
        EntryTransactionType = EntryTransactionType,
        ExitTransactionType = ExitTransactionType,
        Exchange = Exchange,
        ProductType = ProductType,
        OrderType = OrderType,
        Duration = Duration,
        Quantity = Quantity,
        EntryPrice = EntryPrice,
        StopLoss = StopLoss,
        InitialStopLoss = InitialStopLoss,
        InitialRiskPerShare = InitialRiskPerShare,
        HighestPrice = HighestPrice,
        LowestPrice = LowestPrice,
        CurrentPrice = CurrentPrice,
        ATR = ATR,
        EntryTime = EntryTime,
        BuyOrderId = BuyOrderId,
        StopLossOrderId = StopLossOrderId,
        UniqueOrderId = UniqueOrderId,
        ExitOrderId = ExitOrderId,
        ExitOrderTag = ExitOrderTag,
        ExitUniqueOrderId = ExitUniqueOrderId,
        BreakEvenPrice = BreakEvenPrice,
        MinimumExitPrice = MinimumExitPrice,
        ReservedCapital = ReservedCapital,
        EstimatedCharges = EstimatedCharges,
        EstimatedEntryCharges = EstimatedEntryCharges,
        EstimatedExitCharges = EstimatedExitCharges,
        Closed = Closed,
        ExitOrderPlaced = ExitOrderPlaced,
        ExitOrderPlacedAt = ExitOrderPlacedAt,
        ExitLastAttemptTime = ExitLastAttemptTime,
        ExitRetryCount = ExitRetryCount,
        ExitRequired = ExitRequired,
        ExitReason = ExitReason,
        ExitPrice = ExitPrice,
        ExitTime = ExitTime,
        ExitTrigger = ExitTrigger,
        ExitTriggerPrice = ExitTriggerPrice,
        EntryRequestedQuantity = EntryRequestedQuantity,
        EntryFilledQuantity = EntryFilledQuantity,
        EntryAveragePrice = EntryAveragePrice,
        EntryExecutionState = EntryExecutionState,
        EntryLastUpdateTime = EntryLastUpdateTime,
        ExitRequestedQuantity = ExitRequestedQuantity,
        ExitFilledQuantity = ExitFilledQuantity,
        ExitAveragePrice = ExitAveragePrice,
        ExitExecutionState = ExitExecutionState,
        ExitLastUpdateTime = ExitLastUpdateTime,
        Status = Status,
        StopLossTriggered = StopLossTriggered,
        StopLossTriggerPrice = StopLossTriggerPrice,
        StopLossTriggerTime = StopLossTriggerTime,
        TrailingActivated = TrailingActivated,
        TrailingActivatedTime = TrailingActivatedTime,
        TrailingMoveCount = TrailingMoveCount,
        TrailingProtectedProfit = TrailingProtectedProfit,
        PeakEstimatedNetProfit = PeakEstimatedNetProfit,
        EligibilityReason = EligibilityReason,
        EntryReason = EntryReason,
        EntryDecision = EntryDecision,
        BuyEligibilityReason = BuyEligibilityReason,
        RejectReason = RejectReason
    };
}
