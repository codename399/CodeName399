namespace CodeName399.Kite.API.Models.Configuration
{
    using MongoDB.Bson.Serialization.Attributes;
    using System;
    using System.Collections.Generic;

    [BsonIgnoreExtraElements]
    public class TradingConfiguration
    {
        public string Id { get; set; } = "DEFAULT";

        public bool EnableAutoTrading { get; set; } = false;

        public bool PaperTrading { get; set; } = true;


        public bool EnableNotification { get; set; } = true;


        public InstrumentTradingSettings Equity { get; set; } = new();

        public decimal RiskPercentage { get; set; } = 2;

        public decimal MaxCapitalPerTradePercent { get; set; } = 10m;

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

        // Equity prediction strategy configuration. These values are the single
        // source of truth for live prediction, replay and model persistence.
        public PredictionTradingSettings Prediction { get; set; } = new();

        // Raw broker market-data archive configuration.
        public RawMarketDataSettings RawMarketData { get; set; } = new();


    }

    public class PredictionTradingSettings
    {
        public bool Enabled { get; set; } = true;
        public decimal TargetNetPercent { get; set; } = 0.25m;
        public decimal RoundTripCostPercent { get; set; } = 0.12m;
        public int FutureWindowSeconds { get; set; } = 300;
        public int MinimumHistoryTicks { get; set; } = 20;
        public int PredictionSpacingSeconds { get; set; } = 5;
        public int MaximumHistoryTicksPerSymbol { get; set; } = 120;
        public int MaximumSamples { get; set; } = 1_000_000;
        public double MinimumBuyProbability { get; set; } = 0.25d;
        public double MinimumHoldProbability { get; set; } = 0.15d;
        public int LiveHistoryTicks { get; set; } = 120;
        public string ModelFileName { get; set; } = "equity-prediction-model.json";
        public string TrainingDataPath { get; set; } = "RawMarketData";
        public double TrainFraction { get; set; } = 0.70d;

        // Automatically build the persisted prediction model from raw market-data
        // archives when no compatible model exists. Prediction remains a hard
        // requirement for BUY; this only manages model readiness.
        public bool AutoTrainModel { get; set; } = true;
        public int AutoTrainCheckIntervalMinutes { get; set; } = 5;
        public int AutoTrainStartupDelaySeconds { get; set; } = 10;
        public int AutoTrainMinimumArchiveAgeMinutes { get; set; } = 2;

        public decimal SimulationStartingCapital { get; set; } = 100000m;
        public decimal SimulationMaximumRiskPerTrade { get; set; } = 10000m;
        public decimal SimulationMaxCapitalPerTradePercent { get; set; } = 20m;
        public decimal SimulationProtectiveStopPercent { get; set; } = 0.50m;
    }
}


    public class RawMarketDataSettings
    {
        public bool Enabled { get; set; } = true;
        public string StoragePath { get; set; } = "RawMarketData";
        public int MaximumZipSizeMb { get; set; } = 100;
        public int RollOverAtPercent { get; set; } = 95;
        public bool RecordOnlyDuringMarketHours { get; set; } = true;
    }
