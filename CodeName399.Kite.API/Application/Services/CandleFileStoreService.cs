using System.Collections.Concurrent;
using System.Text.Json;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Application.Services;

/// <summary>Persistent JSON storage for candles downloaded from the historical API.</summary>
public sealed class CandleFileStoreService
{
    private readonly ILogger<CandleFileStoreService> _logger;
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public CandleFileStoreService(ILogger<CandleFileStoreService> logger)
    {
        _logger = logger;
        var configured = Environment.GetEnvironmentVariable("CODENAME399_CANDLE_STORAGE_PATH");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("CODENAME399_CANDLE_STORAGE_PATH is not set. Configure this environment variable for historical candle storage.");
        _directory = Path.GetFullPath(configured.Trim());
        Directory.CreateDirectory(_directory);
        _logger.LogInformation("Historical candle JSON storage configured: {CandleDirectory}", _directory);
    }

    public async Task<List<Candle>> GetAsync(string token, string symbol, string interval, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return [];
        var gate = _locks.GetOrAdd(Key(token, interval), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try { return await ReadFileAsync(GetPath(token, interval), ct); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to read historical candle file. Symbol={Symbol} Token={Token}", symbol, token);
            return [];
        }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(string token, string symbol, string interval, IEnumerable<Candle> candles, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token) || candles == null) return;
        var gate = _locks.GetOrAdd(Key(token, interval), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var path = GetPath(token, interval);
            var existing = await ReadFileAsync(path, ct);
            var merged = existing.Concat(candles).Where(IsValid)
                .GroupBy(c => c.Timestamp).Select(g => g.Last())
                .OrderBy(c => c.Timestamp).ToList();
            await WriteFileAsync(path, merged, ct);
            _logger.LogInformation("HISTORICAL CANDLES SAVED Symbol={Symbol} Token={Token} Interval={Interval} Count={Count}", symbol, token, interval, merged.Count);
        }
        finally { gate.Release(); }
    }

    private async Task WriteFileAsync(string path, List<Candle> candles, CancellationToken ct)
    {
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await JsonSerializer.SerializeAsync(stream, candles, _json, ct);
            File.Move(temp, path, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private async Task<List<Candle>> ReadFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return [];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, true);
        var loaded = await JsonSerializer.DeserializeAsync<List<Candle>>(stream, _json, ct) ?? [];
        return loaded.Where(IsValid).GroupBy(c => c.Timestamp).Select(g => g.Last()).OrderBy(c => c.Timestamp).ToList();
    }

    private string GetPath(string token, string interval)
    {
        static string Safe(string value) => string.Concat((value ?? "unknown").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_directory, Safe(token) + "_" + Safe(interval) + ".json");
    }
    private static string Key(string token, string interval) => token + "|" + interval;
    private static bool IsValid(Candle c) => c != null && c.Open > 0m && c.Close > 0m && c.High >= c.Low;

    public static TimeSpan IntervalDuration(string interval) => interval?.ToUpperInvariant() switch
    {
        "ONE_MINUTE" or "1MIN" or "1M" => TimeSpan.FromMinutes(1),
        "FIVE_MINUTE" or "5MIN" or "5M" => TimeSpan.FromMinutes(5),
        "THIRTY_MINUTE" or "30MIN" or "30M" => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromMinutes(1)
    };
}
