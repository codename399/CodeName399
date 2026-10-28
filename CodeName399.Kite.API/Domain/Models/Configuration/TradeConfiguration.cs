namespace CodeName399.Kite.API.Models.Configuration
{
    using MongoDB.Bson.Serialization.Attributes;
    using System;
    using System.Collections.Generic;

    [BsonIgnoreExtraElements]
    public class FixedQuantityPriceBand
    {
        public decimal StartPrice { get; set; }
        public decimal EndPrice { get; set; }
        public int Quantity { get; set; }
    }

    [BsonIgnoreExtraElements]
    public class TradingConfiguration
    {
        public string Id { get; set; } = "DEFAULT";

        public bool EnableAutoTrading { get; set; } = false;

        public bool PaperTrading { get; set; } = true;

        /// <summary>Runs paper orders through the same trade lifecycle, monitoring and notifications as live orders without submitting them to the broker.</summary>
        public bool PaperTradingAsLive { get; set; } = true;


        public bool EnableNotification { get; set; } = true;


        public InstrumentTradingSettings Equity { get; set; } = new();

        public decimal RiskPercentage { get; set; } = 2;

        public decimal MaxCapitalPerTradePercent { get; set; } = 100m;

        public int MaxBrokerFailuresBeforeKillSwitch { get; set; } = 5;
        public int BrokerFailureWindowMinutes { get; set; } = 2;

        public bool IgnoreMarketHours { get; set; } = false;

        public TimeSpan MarketOpenTime { get; set; } = new TimeSpan(9, 15, 0);

        // Equity market close. New intraday entries are cut off before auto-square-off.
        public TimeSpan MarketCloseTime { get; set; } = new TimeSpan(15, 30, 0);
        public TimeSpan IntradayEntryCutoffTime { get; set; } = new TimeSpan(15, 20, 0);
        public TimeSpan EquityMisAutoSquareOffTime { get; set; } = new TimeSpan(15, 10, 0);
        public TimeSpan RoboAutoSquareOffTime { get; set; } = new TimeSpan(15, 05, 0);

        // CAS window documented by SmartAPI effective 03-Aug-2026.
        public TimeSpan CasTransitionStart { get; set; } = new TimeSpan(15, 15, 0);
        public TimeSpan CasOrderEntryStart { get; set; } = new TimeSpan(15, 20, 0);
        public TimeSpan CasMarketOnlyEnd { get; set; } = new TimeSpan(15, 25, 0);
        public TimeSpan CasLimitOnlyEnd { get; set; } = new TimeSpan(15, 30, 0);
        public TimeSpan CasRandomCloseSafetyCutoff { get; set; } = new TimeSpan(15, 28, 0);
        public TimeSpan CasEnd { get; set; } = new TimeSpan(15, 40, 0);
        public TimeSpan CasPostCloseEnd { get; set; } = new TimeSpan(16, 00, 0);
        public decimal CasPriceBandPercent { get; set; } = 3m;

        public int WatchListRefreshMinutes { get; set; } = 1;

        public List<string> ExcludedSymbols { get; set; } =
            new List<string> { "TATSILV-EQ", "AONEGOLD-EQ", "ACUTAAS-EQ", "IDEA-EQ" };

        public int MaxCandidates { get; set; } = 500;

        /// <summary>Maximum stock price allowed in the top-gainers discovery shortlist.</summary>
        public decimal MaximumStockPrice { get; set; } = 50m;

        /// <summary>Fixed selects quantity from FixedQuantityPriceBands; Dynamic preserves the existing risk-based quantity calculation.</summary>
        public string QuantityType { get; set; } = "Fixed";

        /// <summary>Price bands used when QuantityType is Fixed. First matching band wins at shared boundaries.</summary>
        public List<FixedQuantityPriceBand> FixedQuantityPriceBands { get; set; } = new()
        {
            new FixedQuantityPriceBand { StartPrice = 0m, EndPrice = 10m, Quantity = 50000 },
            new FixedQuantityPriceBand { StartPrice = 10m, EndPrice = 20m, Quantity = 30000 },
            new FixedQuantityPriceBand { StartPrice = 20m, EndPrice = 999999m, Quantity = 10000 }
        };

        public DateOnly? LastDailySummarySent { get; set; } = null;

        // Timestamp of the last successful Angel One instrument-master load.
        // Stored in configuration so the daily load state survives application restarts.
        public DateTime? InstrumentLoadedAt { get; set; } = null;

        public string MarketTimeZoneId { get; set; } = "Asia/Kolkata";
        public List<DateOnly> TradingHolidays { get; set; } = [];
        public decimal MaximumTotalOpenRisk { get; set; } = 10000m;
        public int RiskReservationSeconds { get; set; } = 10;
        // Revalidate a candidate against the latest quote immediately before an
        // entry order is submitted. This prevents stale signals from becoming
        // market orders after the price has already moved materially against them.
        // Persist a compact execution audit on every attempted entry. The audit is
        // observational only and does not change trading decisions.
        public bool EnableEntryExecutionAudit { get; set; } = true;
        public bool EnableScripConsentForCashOrders { get; set; } = true;
        public int WebSocketHeartbeatSeconds { get; set; } = 10;
        public int WebSocketPongTimeoutSeconds { get; set; } = 30;
        public int WebSocketRetryInitialSeconds { get; set; } = 10;
        public int WebSocketRetryMaxSeconds { get; set; } = 60;
        public int BrokerPositionConfirmationDelaySeconds { get; set; } = 1;
        public int SquareOffRetryDelaySeconds { get; set; } = 1;
        public int StopLossConfirmationSeconds { get; set; } = 2;


        public int BuyTradingInterval { get; set; } = 1000;

        public int SellTradingInterval { get; set; } = 1000;

        public List<string>? VisibleColumns { get; set; } = [];



        public bool AutoSquareOff { get; set; } = true;

        public decimal PaperTradingBalance { get; set; } = 100000m;


        public ExitSettings Exit { get; set; } = new();

        public string ResearchDataVersion { get; set; } = "v1";

        // Raw broker market-data archive configuration.
        public RawMarketDataSettings RawMarketData { get; set; } = new();


    }

    public class RawMarketDataSettings
    {
        public bool Enabled { get; set; } = true;
        public string StoragePath { get; set; } = "RawMarketData";
        public int MaximumZipSizeMb { get; set; } = 100;
        public int RollOverAtPercent { get; set; } = 95;
        public bool RecordOnlyDuringMarketHours { get; set; } = true;
    }
}
