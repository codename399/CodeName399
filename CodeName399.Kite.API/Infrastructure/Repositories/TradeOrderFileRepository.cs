using System.Text.Json;
using CodeName399.Kite.API.Domain.Models;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;

namespace CodeName399.Kite.API.Repository;

/// <summary>
/// File-backed CRUD store for active TradeOrder recovery state.
/// MongoDB is deliberately not used for TradeOrder persistence.
/// Each active trade has exactly one JSON file. Closing a trade deletes it.
/// </summary>
public sealed class TradeOrderFileRepository : ITradeOrderRepository
{
    private readonly string _rootPath;
    private readonly ILogger<TradeOrderFileRepository> _logger;
    private readonly SemaphoreSlim _ioLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public TradeOrderFileRepository(ILogger<TradeOrderFileRepository> logger)
    {
        _logger = logger;
        _rootPath = Environment.GetEnvironmentVariable("CODENAME399_TRADE_RECOVERY_PATH")
            ?? Path.Combine(AppContext.BaseDirectory, "TradeRecovery");
        Directory.CreateDirectory(_rootPath);
    }

    private string FilePath(string tradeId) => Path.Combine(_rootPath, $"trade-{Sanitize(tradeId)}.json");

    private static string Sanitize(string value)
        => string.Concat(value.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_'));

    private static string EnsureTradeId(TradeOrder trade)
    {
        if (!string.IsNullOrWhiteSpace(trade.Id)) return trade.Id;
        trade.Id = Guid.NewGuid().ToString("N");
        return trade.Id;
    }

    private async Task WriteAsync(TradeOrder trade)
    {
        var id = EnsureTradeId(trade);
        var record = TradeOrderRecoveryRecord.From(trade);
        var target = FilePath(id);
        var temp = target + ".tmp";
        var json = JsonSerializer.Serialize(record, _jsonOptions);

        await _ioLock.WaitAsync();
        try
        {
            await File.WriteAllTextAsync(temp, json);
            File.Move(temp, target, true);
        }
        finally
        {
            _ioLock.Release();
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    private async Task<List<TradeOrder>> ReadAllAsync()
    {
        var result = new List<TradeOrder>();
        foreach (var path in Directory.EnumerateFiles(_rootPath, "trade-*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = await File.ReadAllTextAsync(path);
                var record = JsonSerializer.Deserialize<TradeOrderRecoveryRecord>(json, _jsonOptions);
                if (record == null || record.Closed) continue;
                var trade = record.ToTradeOrder();
                trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();
                result.Add(trade);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unable to read trade recovery file {Path}", path);
            }
        }
        return result;
    }

    public async Task SaveAsync(TradeOrder trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        if (trade.Closed) throw new InvalidOperationException("Closed TradeOrder instances are not persisted.");
        trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();
        await WriteAsync(trade);
    }

    public async Task<TradeOrder?> GetAsync(string symbol)
        => (await ReadAllAsync()).FirstOrDefault(x => string.Equals(x.Symbol, symbol.Trim(), StringComparison.OrdinalIgnoreCase));

    public Task<List<TradeOrder>> GetOpenTradesAsync() => ReadAllAsync();

    public async Task UpdateAsync(TradeOrder trade)
    {
        ArgumentNullException.ThrowIfNull(trade);
        if (trade.Closed)
        {
            await DeleteAsync(trade.Id);
            return;
        }
        await WriteAsync(trade);
    }

    public async Task DeleteAsync(string symbolOrTradeId)
    {
        if (string.IsNullOrWhiteSpace(symbolOrTradeId)) return;
        await _ioLock.WaitAsync();
        try
        {
            var direct = FilePath(symbolOrTradeId);
            if (File.Exists(direct))
            {
                File.Delete(direct);
                return;
            }

            foreach (var path in Directory.EnumerateFiles(_rootPath, "trade-*.json"))
            {
                try
                {
                    var record = JsonSerializer.Deserialize<TradeOrderRecoveryRecord>(await File.ReadAllTextAsync(path), _jsonOptions);
                    if (record != null && string.Equals(record.Symbol, symbolOrTradeId.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(path);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Unable to inspect trade recovery file {Path} while deleting {Key}", path, symbolOrTradeId);
                }
            }
        }
        finally { _ioLock.Release(); }
    }

    public async Task CloseTradeAsync(string symbol) => await DeleteAsync(symbol);

    public async Task<Dictionary<string, TradeOrder>> GetAllOpenAsync()
        => (await ReadAllAsync()).ToDictionary(PositionIdentity.Create, StringComparer.OrdinalIgnoreCase);

    public async Task UpdateManyAsync(IEnumerable<TradeOrder> trades)
    {
        foreach (var trade in trades) await UpdateAsync(trade);
    }

    public async Task<List<TradeOrder>> GetPendingTradesAsync()
        => (await ReadAllAsync()).Where(x => x.Status == TradeStatus.PendingBrokerConfirmation).ToList();

    public async Task UpdateStatusAsync(string symbol, TradeStatus status)
    {
        var trade = await GetAsync(symbol);
        if (trade == null) return;
        trade.Status = status;
        await UpdateAsync(trade);
    }

}
