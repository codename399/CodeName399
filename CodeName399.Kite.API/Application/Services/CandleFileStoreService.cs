using System.Collections.Concurrent;
using System.Text.Json;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.WebSocket.Interface;

namespace CodeName399.Kite.API.Application.Services;

/// <summary>Persists one candle JSON file per instrument and extends the active candle from live ticks.</summary>
public sealed class CandleFileStoreService : BackgroundService
{
    private readonly IAngelWebSocketClient _webSocket;
    private readonly ILogger<CandleFileStoreService> _logger;
    private readonly TradingSettingsProvider _settings;
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, List<Candle>> _candles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastDayVolume = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _symbolsByToken = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    public CandleFileStoreService(IAngelWebSocketClient webSocket, IWebHostEnvironment environment, TradingSettingsProvider settings, ILogger<CandleFileStoreService> logger)
    {
        _webSocket = webSocket;
        _settings = settings;
        _logger = logger;
        _directory = Path.Combine(environment.ContentRootPath, "App_Data", "Candles");
        Directory.CreateDirectory(_directory);
    }

    public async Task<List<Candle>> GetAsync(string token, string symbol, string interval, CancellationToken ct = default)
    {
        var key = Key(token, interval);
        if (!string.IsNullOrWhiteSpace(symbol)) _symbolsByToken[token] = symbol;
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (_candles.TryGetValue(key, out var inMemory)) return inMemory.OrderBy(x => x.Timestamp).ToList();
            var path = GetPath(token, symbol, interval);
            if (!File.Exists(path)) return [];
            await using var stream = File.OpenRead(path);
            var data = await JsonSerializer.DeserializeAsync<List<Candle>>(stream, _json, ct) ?? [];
            _candles[key] = data;
            return data.OrderBy(x => x.Timestamp).ToList();
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Unable to read candle cache for {Symbol} token {Token}", symbol, token); return []; }
        finally { gate.Release(); }
    }

    public async Task SaveAsync(string token, string symbol, string interval, IEnumerable<Candle> candles, CancellationToken ct = default)
    {
        var key = Key(token, interval);
        if (!string.IsNullOrWhiteSpace(symbol)) _symbolsByToken[token] = symbol;
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var existing = _candles.TryGetValue(key, out var current) ? current : await ReadFileAsync(GetPath(token, symbol, interval), ct);
            var merged = existing.Concat(candles ?? []).Where(IsValid).GroupBy(x => x.Timestamp).Select(g => g.Last()).OrderBy(x => x.Timestamp).ToList();
            _candles[key] = merged;
            var path = GetPath(token, symbol, interval);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
                await JsonSerializer.SerializeAsync(stream, merged, _json, ct);
            File.Move(temp, path, true);
        }
        finally { gate.Release(); }
    }

    public async Task AddTickAsync(MarketTick tick, string interval, CancellationToken ct = default)
    {
        if (tick.Ltp <= 0 || string.IsNullOrWhiteSpace(tick.SymbolToken)) return;
        var key = Key(tick.SymbolToken, interval);
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var symbol = !string.IsNullOrWhiteSpace(tick.TradingSymbol)
                ? tick.TradingSymbol
                : _symbolsByToken.TryGetValue(tick.SymbolToken, out var knownSymbol) ? knownSymbol : "unknown";
            if (!string.IsNullOrWhiteSpace(tick.TradingSymbol)) _symbolsByToken[tick.SymbolToken] = tick.TradingSymbol;
            if (!_candles.TryGetValue(key, out var list))
            {
                list = await ReadFileAsync(GetPath(tick.SymbolToken, symbol, interval), ct);
                _candles[key] = list;
            }
            var time = tick.ExchangeTime != default ? tick.ExchangeTime : tick.LastUpdated;
            if (time == default) time = DateTime.UtcNow;
            if (time.Kind == DateTimeKind.Utc) time = TimeZoneInfo.ConvertTimeFromUtc(time, India);
            else if (time.Kind == DateTimeKind.Local) time = TimeZoneInfo.ConvertTime(time, India);
            var start = Floor(time, IntervalDuration(interval));
            var candle = list.LastOrDefault();
            if (candle == null || candle.Timestamp != start)
            {
                candle = new Candle { Timestamp = start, Open = tick.Ltp, High = tick.Ltp, Low = tick.Ltp, Close = tick.Ltp };
                list.Add(candle);
            }
            else
            {
                candle.High = Math.Max(candle.High, tick.Ltp);
                candle.Low = Math.Min(candle.Low, tick.Ltp);
                candle.Close = tick.Ltp;
            }
            decimal addedVolume = 0;
            if (tick.DayVolume > 0)
            {
                // DayVolume is cumulative. Convert it to a per-tick delta for this candle.
                var prior = _lastDayVolume.GetOrAdd(tick.SymbolToken, tick.DayVolume);
                addedVolume = Math.Max(0, tick.DayVolume - prior);
                _lastDayVolume[tick.SymbolToken] = tick.DayVolume;
            }
            if (addedVolume <= 0 && tick.LastTradedQuantity > 0) addedVolume = tick.LastTradedQuantity;
            candle.Volume += addedVolume;
            await PersistLockedAsync(tick.SymbolToken, symbol, interval, list, ct);
        }
        finally { gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _webSocket.TickReceived += OnTick;
        try { await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken); }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally { _webSocket.TickReceived -= OnTick; }
    }

    private void OnTick(object? sender, MarketTick tick)
    {
        var interval = string.IsNullOrWhiteSpace(_settings.Current.CandleInterval) ? "ONE_MINUTE" : _settings.Current.CandleInterval;
        _ = AddTickSafeAsync(tick, interval);
    }

    private async Task AddTickSafeAsync(MarketTick tick, string interval)
    {
        try { await AddTickAsync(tick, interval); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to update candle JSON from tick. Token={Token}", tick.SymbolToken); }
    }

    private async Task PersistLockedAsync(string token, string symbol, string interval, List<Candle> list, CancellationToken ct)
    {
        var path = GetPath(token, symbol, interval);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 65536, true))
            await JsonSerializer.SerializeAsync(stream, list, _json, ct);
        File.Move(temp, path, true);
    }

    private async Task<List<Candle>> ReadFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return [];
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<Candle>>(stream, _json, ct) ?? [];
    }

    private string GetPath(string token, string symbol, string interval)
    {
        static string Safe(string value) => string.Concat((value ?? "unknown").Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_directory, Safe(symbol) + "_" + Safe(token) + "_" + Safe(interval) + ".json");
    }
    private static string Key(string token, string interval) => token + "|" + interval;
    private static bool IsValid(Candle x) => x != null && x.Close > 0 && x.High >= x.Low;
    private static DateTime Floor(DateTime value, TimeSpan span) => new(value.Year, value.Month, value.Day, value.Hour, value.Minute / (int)span.TotalMinutes * (int)span.TotalMinutes, 0, DateTimeKind.Unspecified);
    public static TimeSpan IntervalDuration(string interval) => interval?.ToUpperInvariant() switch { "ONE_MINUTE" or "1MIN" or "1M" => TimeSpan.FromMinutes(1), "FIVE_MINUTE" or "5MIN" or "5M" => TimeSpan.FromMinutes(5), "THIRTY_MINUTE" or "30MIN" or "30M" => TimeSpan.FromMinutes(30), _ => TimeSpan.FromMinutes(1) };
}
