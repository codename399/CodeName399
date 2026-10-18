using CodeName399.Kite.API.Models.Indicator;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CodeName399.Kite.API.Services.Indicator;

public sealed class HistoricalCacheService
{
    private readonly ConcurrentDictionary<string, CachedHistoricalData> _cache = new();
    private readonly ConcurrentDictionary<string, object> _fileLocks = new();

    private readonly string _cacheDirectory;
    private readonly string _pendingFile;
    private readonly object _pendingFileLock = new();
    private readonly MarketClock _clock;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public HistoricalCacheService(IHostEnvironment environment, MarketClock clock)
    {
        _clock = clock;

        _cacheDirectory = Path.Combine(
            environment.ContentRootPath,
            "Data",
            "HistoricalCache");

        _pendingFile = Path.Combine(_cacheDirectory, "pending.json");

        Directory.CreateDirectory(_cacheDirectory);

        LoadPersistedCache();
    }

    public bool TryGet(string symbolToken, out CachedHistoricalData history)
    {
        history = null!;

        if (!_cache.TryGetValue(symbolToken, out history))
            return false;

        return history.IsReady;
    }

    public bool Exists(string symbolToken)
    {
        return _cache.ContainsKey(symbolToken);
    }

    public void AddPending(string symbolToken)
    {
        if (string.IsNullOrWhiteSpace(symbolToken))
            return;

        if (_cache.TryGetValue(symbolToken, out var existing) && existing.IsReady)
            return;

        _cache.TryAdd(
            symbolToken,
            new CachedHistoricalData
            {
                SymbolToken = symbolToken,
                IsReady = false
            });

        PersistPending();
    }

    public IReadOnlyCollection<string> GetStaleTokens(TimeSpan maxAge)
    {
        var cutoff = _clock.Now - maxAge;

        return _cache.Values
            .Where(x => x.IsReady
                && x.Candles1m.Count > 0
                && x.Candles1m[^1].Timestamp < cutoff)
            .Select(x => x.SymbolToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyCollection<string> GetPendingTokens()
    {
        return _cache.Values
            .Where(x => !x.IsReady && !string.IsNullOrWhiteSpace(x.SymbolToken))
            .Select(x => x.SymbolToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public void Set(CachedHistoricalData history)
    {
        if (string.IsNullOrWhiteSpace(history.SymbolToken))
            return;

        history.IsReady = true;

        if (history.Last1MinuteRefresh == default)
            history.Last1MinuteRefresh = DateTime.UtcNow;

        if (history.Last5MinuteRefresh == default)
            history.Last5MinuteRefresh = DateTime.UtcNow;

        if (history.LastPriceUpdate == default)
            history.LastPriceUpdate = DateTime.UtcNow;

        _cache.AddOrUpdate(
            history.SymbolToken,
            history,
            (_, _) => history);

        Persist(history.SymbolToken);
        RemovePending(history.SymbolToken);
    }

    public bool UpdateSnapshot(
        string symbolToken,
        IndicatorSnapshot snapshot)
    {
        if (!_cache.TryGetValue(symbolToken, out var history) || !history.IsReady)
            return false;

        history.UpdateSnapshot(snapshot);
        return true;
    }

    public bool UpdatePrice(
        string symbolToken,
        decimal currentPrice,
        decimal currentVolume)
    {
        if (!_cache.TryGetValue(symbolToken, out var history) || !history.IsReady)
            return false;

        history.CurrentPrice = currentPrice;
        history.LastPriceUpdate = DateTime.UtcNow;

        return true;
    }

    /// <summary>
    /// Persist the current in-memory state for one symbol.
    /// Intended to be called on candle boundaries, not on every tick.
    /// </summary>
    public bool Persist(string symbolToken)
    {
        if (!_cache.TryGetValue(symbolToken, out var history) || !history.IsReady)
            return false;

        var fileLock = _fileLocks.GetOrAdd(
            symbolToken,
            _ => new object());

        lock (fileLock)
        {
            try
            {
                Directory.CreateDirectory(_cacheDirectory);

                var file = GetCacheFile(symbolToken);
                var temp = file + ".tmp";

                var json = JsonSerializer.Serialize(history, _jsonOptions);
                File.WriteAllText(temp, json);

                File.Move(temp, file, true);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public IEnumerable<CachedHistoricalData> GetAll()
    {
        return _cache.Values;
    }

    public IEnumerable<CachedHistoricalData> GetReady()
    {
        return _cache.Values.Where(x => x.IsReady);
    }

    public bool Remove(string symbolToken)
    {
        var removed = _cache.TryRemove(symbolToken, out _);

        RemovePending(symbolToken);

        var fileLock = _fileLocks.GetOrAdd(
            symbolToken,
            _ => new object());

        lock (fileLock)
        {
            try
            {
                var file = GetCacheFile(symbolToken);

                if (File.Exists(file))
                    File.Delete(file);
            }
            catch
            {
                // A failed cleanup must not bring down the background worker.
            }
        }

        return removed;
    }

    public void Clear()
    {
        _cache.Clear();

        lock (_pendingFileLock)
        {
            try
            {
                if (Directory.Exists(_cacheDirectory))
                {
                    foreach (var file in Directory.GetFiles(_cacheDirectory, "*.history.json"))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }

                if (File.Exists(_pendingFile))
                    File.Delete(_pendingFile);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    public bool NeedsRefresh(
        string symbolToken,
        string interval)
    {
        if (!_cache.TryGetValue(symbolToken, out var history))
            return true;

        if (!history.IsReady)
            return false;

        var now = DateTime.UtcNow;

        return interval switch
        {
            "ONE_MINUTE" =>
                now - history.Last1MinuteRefresh >= TimeSpan.FromMinutes(1),

            "FIVE_MINUTE" =>
                now - history.Last5MinuteRefresh >= TimeSpan.FromMinutes(5),

            _ => true
        };
    }

    public void MarkRefresh(
        string symbolToken,
        string interval)
    {
        if (!_cache.TryGetValue(symbolToken, out var history))
            return;

        switch (interval)
        {
            case "ONE_MINUTE":
                history.Last1MinuteRefresh = DateTime.UtcNow;
                break;

            case "FIVE_MINUTE":
                history.Last5MinuteRefresh = DateTime.UtcNow;
                break;
        }
    }

    public int Count => _cache.Count;

    private string GetCacheFile(string symbolToken)
    {
        // Token is broker supplied but still restrict the persisted filename
        // to safe characters.
        var safeToken = new string(
            symbolToken
                .Where(char.IsLetterOrDigit)
                .ToArray());

        if (string.IsNullOrWhiteSpace(safeToken))
            throw new InvalidOperationException("Invalid historical cache token.");

        return Path.Combine(
            _cacheDirectory,
            $"{safeToken}.history.json");
    }

    private void RemovePending(string symbolToken)
    {
        lock (_pendingFileLock)
        {
            var pending = ReadPendingUnsafe();

            if (!pending.Remove(symbolToken))
                return;

            WritePendingUnsafe(pending);
        }
    }

    private void PersistPending()
    {
        lock (_pendingFileLock)
        {
            var pending = _cache.Values
                .Where(x => !x.IsReady && !string.IsNullOrWhiteSpace(x.SymbolToken))
                .Select(x => x.SymbolToken)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            WritePendingUnsafe(pending);
        }
    }

    private HashSet<string> ReadPendingUnsafe()
    {
        try
        {
            if (!File.Exists(_pendingFile))
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(_pendingFile);

            var values =
                JsonSerializer.Deserialize<HashSet<string>>(json, _jsonOptions);

            return values == null
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(
                    values,
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void WritePendingUnsafe(HashSet<string> pending)
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);

            var temp = _pendingFile + ".tmp";

            File.WriteAllText(
                temp,
                JsonSerializer.Serialize(pending, _jsonOptions));

            File.Move(temp, _pendingFile, true);
        }
        catch
        {
            // Persistence is best effort. In-memory trading state remains valid.
        }
    }

    private void LoadPersistedCache()
    {
        try
        {
            Directory.CreateDirectory(_cacheDirectory);

            foreach (var file in Directory.GetFiles(
                _cacheDirectory,
                "*.history.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);

                    var history =
                        JsonSerializer.Deserialize<CachedHistoricalData>(
                            json,
                            _jsonOptions);

                    if (
                        history == null
                        || string.IsNullOrWhiteSpace(history.SymbolToken)
                        || history.Candles1m.Count == 0
                        || history.Candles5m.Count == 0)
                    {
                        continue;
                    }

                    history.IsReady = true;

                    _cache[history.SymbolToken] = history;
                }
                catch
                {
                    // One corrupt symbol must not prevent the remaining
                    // historical caches from loading.
                }
            }

            // Recreate pending markers after loading ready caches.
            foreach (var token in ReadPendingUnsafe())
            {
                if (!_cache.ContainsKey(token))
                {
                    _cache[token] = new CachedHistoricalData
                    {
                        SymbolToken = token,
                        IsReady = false
                    };
                }
            }
        }
        catch
        {
            // Startup must not fail solely because historical persistence
            // is unavailable.
        }
    }
}
