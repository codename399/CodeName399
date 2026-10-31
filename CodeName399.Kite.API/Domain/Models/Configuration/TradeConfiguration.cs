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
        public bool EnableNotification { get; set; } = true;
        public int ChargeCoverLowestPriceChangeCandles { get; set; } = 5;
        public TimeSpan MarketOpenTime { get; set; } = new TimeSpan(9, 15, 0);
        public TimeSpan MarketCloseTime { get; set; } = new TimeSpan(15, 30, 0);
        /// <summary>Last time for new intraday entries: always 30 minutes before market close.</summary>
        public TimeSpan IntradayCutoffTime => MarketCloseTime - TimeSpan.FromMinutes(30);
        /// <summary>Exchange trading holidays. Dates are interpreted in the configured market timezone.</summary>
        public List<DateOnly> TradingHolidays { get; set; } = new();
        public List<string> ExcludedSymbols { get; set; } =
            new List<string> { "TATSILV-EQ", "AONEGOLD-EQ", "ACUTAAS-EQ", "IDEA-EQ" };
        public int MaxCandidates { get; set; } = 10;
        public int CandleBatchSize { get; set; } = 2;
        public int BatchDelay { get; set; } = 1;
        public int BuyBatchSize { get; set; } = 5;
        public string CandleInterval { get; set; } = "ONE_MINUTE";
        public DateOnly? LastDailySummarySent { get; set; } = null;
        public DateTime? InstrumentLoadedAt { get; set; } = null;
        public string MarketTimeZoneId { get; set; } = "Asia/Kolkata";
        public int WebSocketHeartbeatSeconds { get; set; } = 10;
        public int WebSocketPongTimeoutSeconds { get; set; } = 30;
        public int WebSocketRetryInitialSeconds { get; set; } = 10;
        public int WebSocketRetryMaxSeconds { get; set; } = 60;
        public List<string>? VisibleColumns { get; set; } = [];
        public bool AutoSquareOff { get; set; } = true;
        public decimal PaperTradingBalance { get; set; } = 100000m;
        public string ResearchDataVersion { get; set; } = "v1";
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
