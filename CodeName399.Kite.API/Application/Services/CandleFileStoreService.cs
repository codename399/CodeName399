using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.WebSocket.Interface;

namespace CodeName399.Kite.API.Application.Services;

/// <summary>
/// Builds OHLC candles from WebSocket ticks. Completed candles are stored only in
/// CODENAME399_CANDLE_STORAGE_PATH. Trading reads completed candles from those files;
/// no in-memory candle history or database is used.
/// </summary>
public sealed class CandleFileStoreService : BackgroundService
{
    private readonly IAngelWebSocketClient _webSocket;
    private readonly ILogger<CandleFileStoreService> _logger;
    private readonly TradingSettingsProvider _settings;
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, Candle> _formingCandles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastDayVolume = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _lastSequence = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _lastFinalizedCandleTimes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _tickCounts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _symbolsByToken = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = false };
    private readonly Channel<MarketTick> _tickQueue = Channel.CreateBounded<MarketTick>(
        new BoundedChannelOptions(100_000)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });
    private long _droppedTicks;
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    public CandleFileStoreService(
        IAngelWebSocketClient webSocket,
        TradingSettingsProvider settings,
        ILogger<CandleFileStoreService> logger)
    {
        _webSocket = webSocket;
        _settings = settings;
        _logger = logger;
        _directory = ResolveConfiguredDirectory(logger);
    }

    private static string ResolveConfiguredDirectory(ILogger logger)
    {
        var configured = Environment.GetEnvironmentVariable("CODENAME399_CANDLE_STORAGE_PATH");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException(
                "CODENAME399_CANDLE_STORAGE_PATH is not set. Candle storage has no fallback paths; configure this environment variable for the service account.");

        var fullPath = Path.GetFullPath(configured.Trim());
        Directory.CreateDirectory(fullPath);
        var probe = Path.Combine(fullPath, $".write-test-{Guid.NewGuid():N}.tmp");
        using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { }
        File.Delete(probe);
        logger.LogInformation("Candle file storage path configured: {CandleDirectory}", fullPath);
        return fullPath;
    }

    /// <summary>Reads the complete completed-candle history directly from this instrument's JSON file.</summary>
    public async Task<List<Candle>> GetAsync(string token, string symbol, string interval, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return [];
        if (!string.IsNullOrWhiteSpace(symbol)) _symbolsByToken[token] = symbol;
        var gate = _locks.GetOrAdd(Key(token, interval), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            return await ReadFileAsync(GetPath(token, interval), ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to read candle file. Symbol={Symbol} Token={Token} Path={Path}",
                symbol, token, GetPath(token, interval));
            return [];
        }
        finally { gate.Release(); }
    }

    /// <summary>Persists supplied candles to the configured file. Used only by explicit data-import tooling.</summary>
    public async Task SaveAsync(string token, string symbol, string interval, IEnumerable<Candle> candles, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        if (!string.IsNullOrWhiteSpace(symbol)) _symbolsByToken[token] = symbol;
        var gate = _locks.GetOrAdd(Key(token, interval), _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            var existing = await ReadFileAsync(GetPath(token, interval), ct);
            // Keep the complete candle history for this instrument in its single file.
            // Existing timestamps are updated; new timestamps are appended logically.
            var merged = existing.Concat(candles ?? [])
                .Where(IsValid)
                .GroupBy(x => x.Timestamp)
                .Select(g => g.Last())
                .OrderBy(x => x.Timestamp)
                .ToList();
            await WriteFileAsync(GetPath(token, interval), merged, ct);
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Builds the currently-forming candle from ticks. When the interval closes,
    /// the completed candle is added to that instrument's existing JSON file.
    /// </summary>
    public async Task AddTickAsync(MarketTick tick, string interval, CancellationToken ct = default)
    {
        if (tick == null || tick.Ltp <= 0 || string.IsNullOrWhiteSpace(tick.SymbolToken)) return;

        var token = tick.SymbolToken.Trim();
        var key = Key(token, interval);
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(tick.TradingSymbol)) _symbolsByToken[token] = tick.TradingSymbol;
            var tickCount = _tickCounts.AddOrUpdate(token, 1, (_, count) => count + 1);
            var tickUtc = ResolveTickUtc(tick, token);
            var indiaTime = TimeZoneInfo.ConvertTimeFromUtc(tickUtc, India);
            var candleStart = Floor(indiaTime, IntervalDuration(interval));

            // A delayed/out-of-order packet must never recreate a candle that has
            // already been persisted. This was particularly important when the
            // broker exchange timestamp stopped advancing while receipt time did.
            if (_lastFinalizedCandleTimes.TryGetValue(key, out var lastFinalized)
                && candleStart <= lastFinalized)
            {
                _logger.LogDebug(
                    "Ignoring tick for an already finalized candle. Symbol={Symbol} Token={Token} TickCandle={TickCandle} LastFinalized={LastFinalized}",
                    _symbolsByToken.GetValueOrDefault(token, "unknown"), token, candleStart, lastFinalized);
                return;
            }

            if (tickCount == 1 || tickCount % 500 == 0)
            {
                _logger.LogInformation(
                    "TICK CANDLE INPUT Symbol={Symbol} Token={Token} TickCount={TickCount} LTP={Ltp} ExchangeTime={ExchangeTime:o} ReceiptTime={ReceiptTime:o} CandleTime={CandleTime}",
                    _symbolsByToken.GetValueOrDefault(token, "unknown"), token, tickCount, tick.Ltp,
                    tick.ExchangeTime, tick.LastUpdated, candleStart);
            }

            if (_formingCandles.TryGetValue(key, out var forming))
            {
                if (candleStart < forming.Timestamp)
                {
                    _logger.LogDebug("Ignoring out-of-order tick. Token={Token} TickCandle={TickCandle} ActiveCandle={ActiveCandle}",
                        token, candleStart, forming.Timestamp);
                    return;
                }

                if (candleStart > forming.Timestamp)
                {
                    // Persist the candle that has just closed before starting its successor.
                    await AppendCompletedCandleLockedAsync(token, interval, forming, ct);
                    _lastFinalizedCandleTimes[key] = forming.Timestamp;
                    var next = CreateCandle(candleStart, tick.Ltp);
                    var delta = GetVolumeDelta(token, tick);
                    next.Volume = delta;
                    _formingCandles[key] = next;
                    _logger.LogInformation("TICK CANDLE CLOSED Symbol={Symbol} Token={Token} Interval={Interval} CandleTime={CandleTime} O={Open} H={High} L={Low} C={Close} V={Volume} Storage={Storage}",
                        _symbolsByToken.GetValueOrDefault(token, "unknown"), token, interval, forming.Timestamp,
                        forming.Open, forming.High, forming.Low, forming.Close, forming.Volume, GetPath(token, interval));
                    return;
                }

                ApplyPrice(forming, tick.Ltp);
                forming.Volume += GetVolumeDelta(token, tick);
                return;
            }

            var initial = CreateCandle(candleStart, tick.Ltp);
            initial.Volume = GetVolumeDelta(token, tick);
            _formingCandles[key] = initial;
            _logger.LogInformation("TICK CANDLE STARTED Symbol={Symbol} Token={Token} Interval={Interval} CandleTime={CandleTime}",
                _symbolsByToken.GetValueOrDefault(token, "unknown"), token, interval, candleStart);
        }
        finally { gate.Release(); }
    }

    /// <summary>
    /// Called by the candle-close trading scheduler before reading files. It adds each
    /// elapsed completed candle to the same instrument file, even if no tick arrived in
    /// the next interval. No synthetic candles are created for intervals with no ticks.
    /// </summary>
    public async Task FinalizeClosedCandlesAsync(string interval, DateTime nowIndia, CancellationToken ct = default)
    {
        var duration = IntervalDuration(interval);
        foreach (var entry in _formingCandles.ToArray())
        {
            if (!entry.Key.EndsWith("|" + interval, StringComparison.OrdinalIgnoreCase)) continue;
            var token = entry.Key[..entry.Key.LastIndexOf('|')];
            var gate = _locks.GetOrAdd(entry.Key, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(ct);
            try
            {
                if (!_formingCandles.TryGetValue(entry.Key, out var current)) continue;
                if (current.Timestamp + duration > nowIndia) continue;
                await AppendCompletedCandleLockedAsync(token, interval, current, ct);
                _lastFinalizedCandleTimes[entry.Key] = current.Timestamp;
                _formingCandles.TryRemove(entry.Key, out _);
                _logger.LogInformation("TICK CANDLE CLOSED BY CLOCK Token={Token} Interval={Interval} CandleTime={CandleTime} O={Open} H={High} L={Low} C={Close} V={Volume} Storage={Storage}",
                    token, interval, current.Timestamp, current.Open, current.High, current.Low, current.Close, current.Volume,
                    GetPath(token, interval));
            }
            finally { gate.Release(); }
        }
    }

    private async Task AppendCompletedCandleLockedAsync(string token, string interval, Candle candle, CancellationToken ct)
    {
        if (!IsValid(candle)) return;
        var path = GetPath(token, interval);
        var candles = await ReadFileAsync(path, ct);
        var existingIndex = candles.FindIndex(x => x.Timestamp == candle.Timestamp);
        if (existingIndex >= 0) candles[existingIndex] = Clone(candle);
        else candles.Add(Clone(candle));
        // Preserve every previously completed candle. A new candle is added to the
        // same per-instrument JSON array; only a duplicate timestamp is replaced.
        candles = candles.OrderBy(x => x.Timestamp).ToList();
        await WriteFileAsync(path, candles, ct);
        _logger.LogInformation(
            "CANDLE FILE APPENDED Symbol={Symbol} Token={Token} Interval={Interval} CandleTime={CandleTime} FileCandleCount={CandleCount} Path={Path}",
            _symbolsByToken.GetValueOrDefault(token, "unknown"), token, interval, candle.Timestamp, candles.Count, path);
    }

    private async Task WriteFileAsync(string path, List<Candle> candles, CancellationToken ct)
    {
        Directory.CreateDirectory(_directory);
        var temp = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await JsonSerializer.SerializeAsync(stream, candles, _json, ct);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private async Task<List<Candle>> ReadFileAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path)) return [];
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 65536, true);
        var loaded = await JsonSerializer.DeserializeAsync<List<Candle>>(stream, _json, ct) ?? [];
        return loaded.Where(IsValid).GroupBy(x => x.Timestamp).Select(g => g.Last())
            .OrderBy(x => x.Timestamp).ToList();
    }

    private decimal GetVolumeDelta(string token, MarketTick tick)
    {
        if (tick.DayVolume > 0)
        {
            while (true)
            {
                if (!_lastDayVolume.TryGetValue(token, out var previous))
                {
                    if (_lastDayVolume.TryAdd(token, tick.DayVolume)) return 0m;
                    continue;
                }
                if (tick.DayVolume < previous)
                {
                    if (_lastDayVolume.TryUpdate(token, tick.DayVolume, previous)) return 0m;
                    continue;
                }
                if (tick.DayVolume == previous) return 0m;
                if (_lastDayVolume.TryUpdate(token, tick.DayVolume, previous)) return tick.DayVolume - previous;
            }
        }

        if (tick.LastTradedQuantity > 0 && tick.SequenceNumber > 0)
        {
            var previous = _lastSequence.GetOrAdd(token, tick.SequenceNumber);
            if (previous == tick.SequenceNumber) return 0m;
            if (_lastSequence.TryUpdate(token, tick.SequenceNumber, previous)) return tick.LastTradedQuantity;
        }
        return 0m;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _webSocket.TickReceived += OnTick;
        var interval = string.IsNullOrWhiteSpace(_settings.Current.CandleInterval)
            ? "ONE_MINUTE"
            : _settings.Current.CandleInterval;
        _logger.LogInformation(
            "Tick-to-file candle builder started. Interval={Interval} Storage={Storage} InMemoryCandleHistory=false Database=false",
            interval, _directory);

        // Finalization is owned by this service, not by the buy-order scheduler.
        // A slow quote scan or a failed buy cycle can therefore never prevent
        // completed tick-built candles from being written to disk.
        var clockFinalizer = RunClockFinalizerAsync(interval, stoppingToken);
        try
        {
            await foreach (var tick in _tickQueue.Reader.ReadAllAsync(stoppingToken))
            {
                try { await AddTickAsync(tick, interval, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to write tick-built candle. Token={Token}", tick.SymbolToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            _webSocket.TickReceived -= OnTick;
            _tickQueue.Writer.TryComplete();

            try
            {
                // Persist any interval that has genuinely elapsed. Do not create
                // a synthetic candle for the still-open interval.
                var nowIndia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India);
                await FinalizeClosedCandlesAsync(interval, nowIndia, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to flush elapsed candles during candle-builder shutdown.");
            }

            try { await clockFinalizer; }
            catch (OperationCanceledException) { }

            if (_droppedTicks > 0)
                _logger.LogWarning("Tick queue stopped with dropped ticks. DroppedTicks={DroppedTicks}", _droppedTicks);

            _logger.LogInformation(
                "Tick-to-file candle builder stopped. Interval={Interval} ActiveCandles={ActiveCandles} TrackedStocks={TrackedStocks}",
                interval, _formingCandles.Count, _tickCounts.Count);
        }
    }

    private async Task RunClockFinalizerAsync(string interval, CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var nowIndia = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India);
                await FinalizeClosedCandlesAsync(interval, nowIndia, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Clock-based candle finalization failed. Interval={Interval}", interval);
            }
        }
    }

    private DateTime ResolveTickUtc(MarketTick tick, string token)
    {
        var receivedUtc = tick.LastUpdated;
        if (receivedUtc == default || receivedUtc < new DateTime(2000, 1, 1))
            receivedUtc = DateTime.UtcNow;
        else if (receivedUtc.Kind == DateTimeKind.Unspecified)
            receivedUtc = DateTime.SpecifyKind(receivedUtc, DateTimeKind.Utc);
        else if (receivedUtc.Kind == DateTimeKind.Local)
            receivedUtc = receivedUtc.ToUniversalTime();

        var exchangeUtc = tick.ExchangeTime;
        if (exchangeUtc != default && exchangeUtc >= new DateTime(2000, 1, 1))
        {
            if (exchangeUtc.Kind == DateTimeKind.Unspecified)
                exchangeUtc = DateTime.SpecifyKind(exchangeUtc, DateTimeKind.Utc);
            else if (exchangeUtc.Kind == DateTimeKind.Local)
                exchangeUtc = exchangeUtc.ToUniversalTime();

            // Exchange timestamps should be close to packet receipt time. If the
            // feed timestamp is stale, malformed, or unexpectedly in the future,
            // use the actual receipt timestamp so all subsequent ticks don't keep
            // collapsing into one old minute candle.
            var difference = (exchangeUtc - receivedUtc).Duration();
            if (difference <= TimeSpan.FromMinutes(2)
                && exchangeUtc <= receivedUtc.AddSeconds(30))
                return exchangeUtc;

            var count = _tickCounts.GetValueOrDefault(token);
            if (count == 0 || count % 500 == 0)
            {
                _logger.LogWarning(
                    "Stale/invalid exchange timestamp; using WebSocket receipt time. Token={Token} ExchangeTime={ExchangeTime:o} ReceiptTime={ReceiptTime:o} DifferenceSeconds={DifferenceSeconds:F1}",
                    token, exchangeUtc, receivedUtc, difference.TotalSeconds);
            }
        }

        return receivedUtc;
    }

    private void OnTick(object? sender, MarketTick tick)
    {
        if (tick == null || tick.Ltp <= 0 || string.IsNullOrWhiteSpace(tick.SymbolToken)) return;
        if (!_tickQueue.Writer.TryWrite(tick))
        {
            var dropped = Interlocked.Increment(ref _droppedTicks);
            if (dropped == 1 || dropped % 1000 == 0)
                _logger.LogWarning("Tick-to-candle queue full. DroppedTicks={DroppedTicks}", dropped);
        }
    }

    private string GetPath(string token, string interval)
    {
        static string Safe(string value) => string.Concat((value ?? "unknown").Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(_directory, Safe(token) + "_" + Safe(interval) + ".json");
    }

    private static string Key(string token, string interval) => token + "|" + interval;
    private static bool IsValid(Candle x) => x != null && x.Close > 0 && x.High >= x.Low;
    private static Candle Clone(Candle x) => new()
    {
        Timestamp = x.Timestamp, Open = x.Open, High = x.High, Low = x.Low, Close = x.Close, Volume = x.Volume
    };
    private static Candle CreateCandle(DateTime timestamp, decimal ltp) => new()
    {
        Timestamp = timestamp, Open = ltp, High = ltp, Low = ltp, Close = ltp, Volume = 0m
    };
    private static void ApplyPrice(Candle candle, decimal ltp)
    {
        candle.High = Math.Max(candle.High, ltp);
        candle.Low = candle.Low <= 0 ? ltp : Math.Min(candle.Low, ltp);
        candle.Close = ltp;
    }
    private static DateTime Floor(DateTime value, TimeSpan span)
    {
        var minutes = Math.Max(1, (int)span.TotalMinutes);
        return new DateTime(value.Year, value.Month, value.Day, value.Hour,
            value.Minute / minutes * minutes, 0, DateTimeKind.Unspecified);
    }
    public static TimeSpan IntervalDuration(string interval) => interval?.ToUpperInvariant() switch
    {
        "ONE_MINUTE" or "1MIN" or "1M" => TimeSpan.FromMinutes(1),
        "FIVE_MINUTE" or "5MIN" or "5M" => TimeSpan.FromMinutes(5),
        "THIRTY_MINUTE" or "30MIN" or "30M" => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromMinutes(1)
    };
}
