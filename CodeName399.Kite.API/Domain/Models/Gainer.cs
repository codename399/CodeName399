using CodeName399.Kite.API.Models.Enum;
namespace CodeName399.Kite.API.Models;

public class Gainer
{
    public string Symbol { get; set; } = "";
    public string SymbolToken { get; set; } = "";
    public InstrumentType InstrumentType { get; set; } = InstrumentType.Equity;
    public TradeDirection Direction { get; set; } = TradeDirection.Long;
    public string EntryTransactionType => Direction == TradeDirection.Short ? "SELL" : "BUY";
    public string Exchange { get; set; } = "NSE";
    public string ProductType { get; set; } = "INTRADAY";
    public string OrderType { get; set; } = "MARKET";
    public string Duration { get; set; } = "DAY";
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }
    public decimal Spread { get; set; }
    public decimal SpreadPercent { get; set; }
    public decimal CurrentPrice { get; set; }
    public DateTime LastTickUpdated { get; set; }
    public decimal PreviousClose { get; set; }
    public decimal ChangePercent { get; set; }
    public decimal ATR { get; set; }
    public decimal RecoveryScore { get; set; }
    public decimal BreakoutStrength { get; set; }
    public string Signal { get; set; } = "";
    public string Risk { get; set; } = "";
    public string Reason { get; set; } = "";
    public string Suggestion { get; set; } = "";
    public decimal StopLoss { get; set; }
    public bool IsOwned { get; set; }
    public int Quantity { get; set; }
    public decimal AveragePrice { get; set; }
    public decimal InvestedAmount { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal ProfitLoss { get; set; }
    public decimal ProfitLossPercentage { get; set; }
    public decimal UpperCircuitLimit { get; set; }
    public decimal LowerCircuitLimit { get; set; }
    public MarketQuoteData Quote { get; set; } = new();
    public int Priority { get; set; }
    public bool IsSubscribed { get; set; }
    public DateTime LastHistoricalUpdate { get; set; }
    public bool HistoricalLoaded { get; set; }

    // Raw latest broker tick state used for UI/live monitoring.
    public int TickExchangeType { get; set; }
    public long TickSequenceNumber { get; set; }
    public DateTime TickExchangeTime { get; set; }
    public decimal TickLtp { get; set; }
    public decimal TickBid { get; set; }
    public decimal TickAsk { get; set; }
    public decimal TickOpen { get; set; }
    public decimal TickHigh { get; set; }
    public decimal TickLow { get; set; }
    public decimal TickClose { get; set; }
    public long TickLastTradedQuantity { get; set; }
    public decimal TickAveragePrice { get; set; }
    public long TickDayVolume { get; set; }
    public double TickTotalBuyQuantity { get; set; }
    public double TickTotalSellQuantity { get; set; }
    public decimal TickPreviousClose { get; set; }
    public long TickVolume { get; set; }
}
