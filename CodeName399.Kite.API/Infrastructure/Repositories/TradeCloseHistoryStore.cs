using System.Text.Json;
using CodeName399.Kite.API.Models;

namespace CodeName399.Kite.API.Repository;

/// <summary>Small non-TradeOrder close history used only by the daily summary.</summary>
public sealed class TradeCloseHistoryStore
{
    private readonly string _rootPath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly ILogger<TradeCloseHistoryStore> _logger;

    public TradeCloseHistoryStore(ILogger<TradeCloseHistoryStore> logger)
    {
        _logger = logger;
        _rootPath = Environment.GetEnvironmentVariable("CODENAME399_TRADE_CLOSE_HISTORY_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "TradeCloseHistory");
        Directory.CreateDirectory(_rootPath);
    }

    public async Task AppendAsync(TradeOrder trade)
    {
        if (trade.ExitTime == null) return;
        var record = new TradeCloseHistoryRecord
        {
            TradeId = trade.Id,
            Symbol = trade.Symbol,
            Quantity = trade.Quantity,
            EntryPrice = trade.EntryPrice,
            ExitPrice = trade.ExitPrice,
            Charges = trade.EstimatedCharges,
            GrossProfit = trade.GrossProfit,
            ProfitLoss = trade.NetProfit,
            EntryTime = trade.EntryTime,
            ExitTime = trade.ExitTime.Value,
        };

        var path = Path.Combine(_rootPath, $"{record.ExitTime:yyyy-MM-dd}.jsonl");
        var line = JsonSerializer.Serialize(record, _json) + Environment.NewLine;
        await _lock.WaitAsync();
        try { await File.AppendAllTextAsync(path, line); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to append closed-trade summary for {Symbol}", trade.Symbol); }
        finally { _lock.Release(); }
    }

    public async Task<List<TradeOrder>> GetAsync(DateTime from, DateTime to)
    {
        var result = new List<TradeOrder>();
        for (var day = from.Date; day < to.Date; day = day.AddDays(1))
        {
            var path = Path.Combine(_rootPath, $"{day:yyyy-MM-dd}.jsonl");
            if (!File.Exists(path)) continue;
            foreach (var line in await File.ReadAllLinesAsync(path))
            {
                try
                {
                    var x = JsonSerializer.Deserialize<TradeCloseHistoryRecord>(line, _json);
                    if (x == null || x.ExitTime < from || x.ExitTime >= to) continue;
                    result.Add(x.ToTradeOrder());
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Skipping malformed closed-trade summary line in {Path}", path); }
            }
        }
        return result.GroupBy(x => x.Id, StringComparer.OrdinalIgnoreCase).Select(x => x.First()).OrderBy(x => x.ExitTime).ToList();
    }

    private sealed class TradeCloseHistoryRecord
    {
        public string TradeId { get; set; } = "";
        public string Symbol { get; set; } = "";
        public int Quantity { get; set; }
        public decimal EntryPrice { get; set; }
        public decimal ExitPrice { get; set; }
        public decimal Charges { get; set; }
        public decimal GrossProfit { get; set; }
        public decimal ProfitLoss { get; set; }
        public DateTime EntryTime { get; set; }
        public DateTime ExitTime { get; set; }

        public TradeOrder ToTradeOrder() => new()
        {
            Id = TradeId,
            Symbol = Symbol,
            Quantity = Quantity,
            EntryPrice = EntryPrice,
            ExitPrice = ExitPrice,
            EstimatedCharges = Charges,
            GrossProfit = GrossProfit,
            NetProfit = ProfitLoss,
            EntryTime = EntryTime,
            ExitTime = ExitTime
        };
    }
}
