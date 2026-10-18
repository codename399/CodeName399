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

        // Paper-as-live uses the simulated portfolio and never submits broker orders.
        public bool PaperTradingAsLive { get; set; } = true;
        public int GainersPageSize { get; set; } = 10;
        public decimal MinimumBuyProfitChangePercent { get; set; } = 0.25m;
        public bool EnablePullbackOpportunityNotifications { get; set; } = false;
        public bool EnableNetProfitOpportunityNotifications { get; set; } = false;

        public bool EnableNotification { get; set; } = true;


        public InstrumentTradingSettings Equity { get; set; } = new();


        public decimal MaxCapitalPerTradePercent { get; set; } = 10m;
        public decimal RiskPercentage { get; set; } = 0.5m;
        public decimal MaximumTotalOpenRisk { get; set; } = 0m;
        public int RiskReservationSeconds { get; set; } = 30;
        public ExitTradingSettings Exit { get; set; } = new();


        public bool IgnoreMarketHours { get; set; } = false;

        public TimeSpan MarketOpenTime { get; set; } = new TimeSpan(9, 15, 0);

        // Equity market close and explicit intraday entry cutoff.
        public TimeSpan MarketCloseTime { get; set; } = new TimeSpan(15, 0, 0);
        public TimeSpan IntradayEntryCutoffTime { get; set; } = new TimeSpan(15, 0, 0);

        public int WatchListRefreshMinutes { get; set; } = 1;

        public DateOnly? LastDailySummarySent { get; set; } = null;

        // Timestamp of the last successful Angel One instrument-master load.
        // Stored in configuration so the daily load state survives application restarts.
        public DateTime? InstrumentLoadedAt { get; set; } = null;

        public List<DateOnly> TradingHolidays { get; set; } = [];
        public bool EnableScripConsentForCashOrders { get; set; } = true;
        public int WebSocketHeartbeatSeconds { get; set; } = 10;
        public int WebSocketPongTimeoutSeconds { get; set; } = 30;
        public int WebSocketRetryInitialSeconds { get; set; } = 10;
        public int WebSocketRetryMaxSeconds { get; set; } = 60;




        public List<string>? VisibleColumns { get; set; } = [];




        public decimal PaperTradingBalance { get; set; } = 100000m;



        public string ResearchDataVersion { get; set; } = "v1";

        // Raw broker market-data archive configuration.
        public RawMarketDataSettings RawMarketData { get; set; } = new();


    }


    public sealed class ExitTradingSettings
    {
        public decimal TrailingStopAtrMultiplier { get; set; } = 1.2m;
        public decimal TrailingProfitRetentionPercent { get; set; } = 50m;
    }

    public class RawMarketDataSettings
    {
        public bool Enabled { get; set; } = true;
        public string StoragePath { get; set; } = "RawMarketData";
        public int MaximumZipSizeMb { get; set; } = 100;
        public int RollOverAtPercent { get; set; } = 95;
    }
}
