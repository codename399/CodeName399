using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.Indicator;
using DatabaseRepository.Model;
using MongoDB.Bson.Serialization.Attributes;

namespace CodeName399.Kite.API.Models
{
    [BsonIgnoreExtraElements]
    public class TradeOrder : Audit
    {
        /// <summary>Technical stock snapshot associated with this trade for regression/analytics flows.</summary>
        [BsonIgnore]

        public Gainer? Stock { get; set; }

        public string Symbol { get; set; } = "";

        public string SymbolToken { get; set; } = "";

        public InstrumentType InstrumentType { get; set; } = InstrumentType.Equity;
        public TradeDirection Direction { get; set; } = TradeDirection.Long;
        public string EntryTransactionType { get; set; } = "BUY";

        // Persist both sides of the transaction so historical P&L analysis never
        // has to infer direction from entry/exit prices.
        public string ExitTransactionType { get; set; } = "SELL";
        public TradeExecutionMode ExecutionMode { get; set; } = TradeExecutionMode.Paper;
        public string Exchange { get; set; } = "NSE";
        public string ProductType { get; set; } = "INTRADAY";
        public string OrderType { get; set; } = "MARKET";
        public string Duration { get; set; } = "DAY";

        public int Quantity { get; set; }

        public decimal EntryPrice { get; set; }

        public decimal StopLoss { get; set; }


        public decimal HighestPrice { get; set; }

        public decimal CurrentPrice { get; set; }

        public DateTime EntryTime { get; set; }

        /// <summary>EMA values captured when the entry signal was accepted.</summary>
        public decimal EntryEma9 { get; set; }
        public decimal EntryEma21 { get; set; }
        public decimal EntryEmaGapPercent { get; set; }
        public DateTime? EmaCrossoverTime { get; set; }

        /// <summary>Entry regime and pullback-recovery confirmation snapshot.</summary>
        public decimal EntryChoppinessIndex { get; set; }
        public decimal EntryStochRsiK { get; set; }
        public decimal EntryStochRsiD { get; set; }
        public decimal EntryPullbackLow { get; set; }
        public decimal EntryRecoverySwingHigh { get; set; }

        public string BuyOrderId { get; set; } = "";

        public string StopLossOrderId { get; set; } = "";


        public bool Closed { get; set; }

        public decimal ExitPrice { get; set; }

        public DateTime? ExitTime { get; set; }

        public string ExitReason { get; set; } = "";

        // Research classification fields are persisted after the observation phase.
        public string EntryCategory { get; set; } = "RESEARCH_UNCLASSIFIED";
        public string ExitCategory { get; set; } = "RESEARCH_UNCLASSIFIED";
        public string EntryCategoryVersion { get; set; } = "v1";
        public string ExitCategoryVersion { get; set; } = "v1";
        public string EntryConfigurationSnapshotJson { get; set; } = "";
        public string ExitConfigurationSnapshotJson { get; set; } = "";
        public string EntryHistoricalContextJson { get; set; } = "";
        public string ExitHistoricalContextJson { get; set; } = "";
        public TradeResearchMetrics Research { get; set; } = new();


        public decimal EstimatedCharges { get; set; }
        public decimal EstimatedEntryCharges { get; set; }
        public decimal EstimatedExitCharges { get; set; }

        /// <summary>Charges calculated for the actual filled entry/exit prices.</summary>
        public decimal RealizedCharges { get; set; }

        /// <summary>Realized gross P&amp;L from the actual filled prices.</summary>
        public decimal RealizedGrossProfit { get; set; }

        /// <summary>Realized net P&amp;L after realized transaction charges.</summary>
        public decimal RealizedNetProfit { get; set; }

        public decimal RealizedRoiPercent { get; set; }

        [BsonIgnore]


        public decimal EstimatedGrossProfit { get; set; }

        [BsonIgnore]


        public decimal EstimatedNetProfit { get; set; }

        [BsonIgnore]


        public decimal ChargesPercent { get; set; }

        public bool ExitOrderPlaced { get; set; }

        public DateTime? ExitOrderPlacedAt { get; set; }

        public DateTime? ExitLastAttemptTime { get; set; }

        public int ExitRetryCount { get; set; }

        public string ExitOrderId { get; set; } = "";

        public decimal BreakEvenPrice { get; set; }

        public decimal MinimumExitPrice { get; set; }

        public string UniqueOrderId { get; set; } = "";
        public string ExitOrderTag { get; set; } = "";
        public string ExitUniqueOrderId { get; set; } = "";
        public decimal ReservedCapital { get; set; }
        public bool ExitRequired { get; set; }
        // Broker execution state. Quantities are cumulative for the broker order.
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

        public void UpdateResearchPath(MarketTick tick)
        {
            if (tick == null || tick.Ltp <= 0 || EntryPrice <= 0 || Quantity <= 0)
                return;

            var executableExitPrice = Direction == TradeDirection.Short
                ? (tick.Ask > 0 ? tick.Ask : tick.Ltp)
                : (tick.Bid > 0 ? tick.Bid : tick.Ltp);
            if (executableExitPrice <= 0) return;

            var gross = Direction == TradeDirection.Short
                ? (EntryPrice - executableExitPrice) * Quantity
                : (executableExitPrice - EntryPrice) * Quantity;
            var charges = Math.Max(0m, EstimatedEntryCharges) + Math.Max(0m, EstimatedExitCharges);
            if (charges <= 0m) charges = Math.Max(0m, EstimatedCharges);
            var net = gross - charges;
            var now = tick.LastUpdated == default ? DateTime.UtcNow : tick.LastUpdated;
            var priceMove = Direction == TradeDirection.Short
                ? EntryPrice - executableExitPrice
                : executableExitPrice - EntryPrice;
            var movePct = EntryPrice > 0m ? priceMove / EntryPrice * 100m : 0m;

            Research.TickCount++;
            Research.PeakPrice = Research.PeakPrice <= 0 ? executableExitPrice : Math.Max(Research.PeakPrice, executableExitPrice);
            Research.TroughPrice = Research.TroughPrice <= 0 ? executableExitPrice : Math.Min(Research.TroughPrice, executableExitPrice);

            if (priceMove > Research.MaxFavorablePriceMove)
            {
                Research.MaxFavorablePriceMove = priceMove;
                Research.MaxFavorableMovePercent = movePct;
            }
            if (priceMove < Research.MaxAdversePriceMove)
            {
                Research.MaxAdversePriceMove = priceMove;
                Research.MaxAdverseMovePercent = movePct;
            }
            Research.MaxFavorableNetProfit = Math.Max(Research.MaxFavorableNetProfit, net);
            Research.MaxAdverseNetProfit = Math.Min(Research.MaxAdverseNetProfit, net);

            if (net > Research.PeakNetProfit)
            {
                Research.PeakNetProfit = net;
                Research.PeakNetProfitTime = now;
                Research.TimeToPeakNetProfitSeconds = Math.Max(0, (now - EntryTime).TotalSeconds);
            }

            var wasPositive = Research.LastWasPositive;
            var isPositive = net > 0m;
            if (isPositive && !wasPositive)
            {
                Research.PositiveNetProfitCrossings++;
                if (!Research.FirstPositiveNetProfitTime.HasValue)
                {
                    Research.FirstPositiveNetProfitTime = now;
                    Research.PriceAtFirstPositiveNetProfit = executableExitPrice;
                }
            }
            if (isPositive) Research.PositiveNetProfitObserved = true;
            if (wasPositive && Research.LastPositiveStateTime.HasValue)
                Research.TimeInPositiveNetProfitSeconds += Math.Max(0, (now - Research.LastPositiveStateTime.Value).TotalSeconds);
            Research.LastWasPositive = isPositive;
            Research.LastPositiveStateTime = isPositive ? now : null;

            if (Research.PeakNetProfit > 0m)
                Research.MaxDrawdownFromPeakNetProfit = Math.Min(Research.MaxDrawdownFromPeakNetProfit, net - Research.PeakNetProfit);

            if (Research.MaxAdverseNetProfit < 0m && !Research.FirstPositiveNetProfitTime.HasValue)
                Research.TimeToMaxAdverseSeconds = Math.Max(0, (now - EntryTime).TotalSeconds);
        }

        [BsonIgnore]


        public bool StateUpdated { get; set; }

        public bool StopLossTriggered { get; set; }

        public decimal StopLossTriggerPrice { get; set; }

        public DateTime? StopLossTriggerTime { get; set; }

        public string EligibilityReason { get; set; } = "";

        public decimal ATR { get; set; }

        [BsonIgnore]

        public bool TrailingActivated { get; set; }

        // Complete indicator snapshot captured at BUY time and persisted for research.
        public TradeIndicatorSnapshot EntryIndicators { get; set; } = new();


        // Preserve original trade setup
        public decimal InitialStopLoss { get; set; }


        public decimal InitialRiskPerShare { get; set; }

        // Trade lifecycle
        public decimal LowestPrice { get; set; }

        [BsonIgnore]


        public DateTime? TrailingActivatedTime { get; set; }

        [BsonIgnore]


        public int TrailingMoveCount { get; set; }

        [BsonIgnore]


        public decimal TrailingProtectedProfit { get; set; }

        // Highest executable-side estimated net profit observed while the trade is open.
        // Used to ratchet the trailing stop so previously earned profits are not given back.
        [BsonIgnore]

        public decimal PeakEstimatedNetProfit { get; set; }

        public string EntryReason { get; set; } = string.Empty;
        public string EntryDecision { get; set; } = string.Empty;
        public string ExitDecision { get; set; } = string.Empty;

        public string BuyEligibilityReason { get; set; } = string.Empty;
        public string? RejectReason { get; set; }
        public string ExitTrigger { get; set; } = string.Empty;
        public decimal ExitTriggerPrice { get; set; }
        // Indicator snapshots
        public TradeIndicatorSnapshot? ExitIndicators { get; set; }

    }
}
