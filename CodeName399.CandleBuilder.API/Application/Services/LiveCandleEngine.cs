using System.Collections.Concurrent;
using System.Threading.Channels;
using System.Text.Json;
using CodeName399.CandleBuilder.API.Models.Indicator;
using CodeName399.CandleBuilder.API.WebSocket.Interface;
using Microsoft.AspNetCore.SignalR;
using CodeName399.CandleBuilder.API.SignalRHubs;

namespace CodeName399.CandleBuilder.API.Services;

/// <summary>
/// Builds candles using exchange event-time. A new minute starts immediately at its
/// boundary; the previous minute remains correctable for a short lateness window.
/// </summary>
public sealed class LiveCandleEngine : IHostedService, IDisposable
{
    private sealed class CandleBucket
    {
        public required Candle Candle { get; init; }
        public DateTime FirstTickUtc { get; set; }
        public DateTime LastTickUtc { get; set; }
        public long LastSequence { get; set; }
    }

    private sealed class State
    {
        public CandleBucket? Current;
        public readonly SortedDictionary<DateTime, CandleBucket> Pending = new();
        public readonly Dictionary<DateTime, CandleBucket> Finalized = new();
        public readonly List<Candle> Minute = [];
    }

    private sealed record CandleWrite(string Token, string Interval, Candle Candle, bool Correction);

    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAngelWebSocketClient _socket;
    private readonly IHubContext<MarketHub> _hub;
    private readonly RawMarketDataArchiveService _rawMarketData;
    private readonly MarketClock _marketClock;
    private readonly ILogger<LiveCandleEngine> _logger;
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _timerCts = new();
    private readonly Channel<CandleWrite> _writes = Channel.CreateUnbounded<CandleWrite>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private Task? _boundaryTask;
    private Task? _writerTask;
    private readonly TimeSpan _finalizationDelay;
    private static readonly TimeZoneInfo India = ResolveIndiaTimeZone();
    private static readonly TimeSpan FinalizedRetention = TimeSpan.FromMinutes(60);

    public LiveCandleEngine(
        IAngelWebSocketClient socket,
        IHubContext<MarketHub> hub,
        RawMarketDataArchiveService rawMarketData,
        MarketClock marketClock,
        ILogger<LiveCandleEngine> logger)
    {
        _socket = socket;
        _hub = hub;
        _rawMarketData = rawMarketData;
        _marketClock = marketClock;
        _logger = logger;

        var configured = Environment.GetEnvironmentVariable("CODENAME399_CANDLE_STORAGE_PATH");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("CODENAME399_CANDLE_STORAGE_PATH must be configured.");

        _directory = Path.GetFullPath(configured);
        Directory.CreateDirectory(_directory);

        var delayMs = 2000;
        var rawDelay = Environment.GetEnvironmentVariable("CODENAME399_CANDLE_FINALIZATION_DELAY_MS");
        if (int.TryParse(rawDelay, out var parsed))
            delayMs = Math.Clamp(parsed, 0, 10_000);
        _finalizationDelay = TimeSpan.FromMilliseconds(delayMs);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _socket.TickReceived += OnTick;
        _writerTask = Task.Run(CandleWriterLoopAsync);
        _boundaryTask = Task.Run(() => RunBoundaryClockAsync(_timerCts.Token));
        _logger.LogInformation(
            "Live candle engine started. Timezone=Asia/Kolkata FinalizationDelayMs={DelayMs}. New candles open immediately at exchange-time minute boundaries.",
            _finalizationDelay.TotalMilliseconds);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _socket.TickReceived -= OnTick;
        _timerCts.Cancel();

        if (_boundaryTask != null)
        {
            try { await _boundaryTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
        }

        _writes.Writer.TryComplete();
        if (_writerTask != null)
        {
            try { await _writerTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
        }
    }

    // Boundary processing is independent from the tick callback. It moves the
    // previous candle to Pending but does not block opening the next minute.
    private async Task RunBoundaryClockAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var nowUtc = DateTime.UtcNow;
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, India);
            var currentMinute = FloorMinute(nowIst);

            foreach (var pair in _states)
            {
                var state = pair.Value;
                lock (state)
                {
                    if (state.Current != null && state.Current.Candle.Timestamp < currentMinute)
                    {
                        var old = state.Current;
                        state.Current = null;
                        state.Pending[old.Candle.Timestamp] = old;
                    }

                    FinalizeDue(pair.Key, state, nowUtc);
                    PruneFinalized(state, nowIst);
                }
            }
        }
    }

    private void OnTick(object? sender, global::MarketTick tick)
    {
        if (tick == null || string.IsNullOrWhiteSpace(tick.SymbolToken) || tick.Ltp <= 0m)
            return;

        // Forward ticks immediately; candle finalization never delays the live tick stream.
        _ = _hub.Clients.All.SendAsync("TickReceived", tick);

        var exchangeUtc = NormalizeUtc(tick.ExchangeTime, tick.ReceivedAtUtc);
        var exchangeIst = TimeZoneInfo.ConvertTimeFromUtc(exchangeUtc, India);

        // Candle generation follows exchange event-time, not server receipt-time.
        // This also allows a delayed in-session tick received after the close to
        // correct its original candle, while rejecting pre/post-market ticks.
        if (!_marketClock.IsRawMarketDataRecordingOpen(exchangeIst))
        {
            _logger.LogDebug(
                "Ignoring tick outside the exchange market session. Token={Token} ExchangeTimeIST={ExchangeTimeIST:yyyy-MM-dd HH:mm:ss.fff} ReceivedAtUtc={ReceivedAtUtc:O}",
                tick.SymbolToken, exchangeIst, tick.ReceivedAtUtc);
            return;
        }

        var minute = FloorMinute(exchangeIst);
        var state = _states.GetOrAdd(tick.SymbolToken, _ => new State());

        lock (state)
        {
            if (state.Current == null)
            {
                state.Current = NewBucket(minute, tick, exchangeUtc);
            }
            else if (minute > state.Current.Candle.Timestamp)
            {
                state.Pending[state.Current.Candle.Timestamp] = state.Current;
                state.Current = NewBucket(minute, tick, exchangeUtc);
            }
            else if (minute == state.Current.Candle.Timestamp)
            {
                Update(state.Current, tick, exchangeUtc);
            }
            else if (state.Pending.TryGetValue(minute, out var pending))
            {
                // Late tick inside the grace period: correct the pending candle.
                Update(pending, tick, exchangeUtc);
            }
            else if (state.Finalized.TryGetValue(minute, out var finalized))
            {
                // Very late tick: correct the stored candle, but do not reopen it.
                Update(finalized, tick, exchangeUtc);
                UpsertMinute(state, finalized.Candle);
                QueueWrite(tick.SymbolToken, "1m", finalized.Candle, correction: true);
                RebuildRollups(tick.SymbolToken, state, minute, correction: true);
                _logger.LogWarning(
                    "Late tick corrected a finalized candle. Token={Token} CandleTimeIST={CandleTime:yyyy-MM-dd HH:mm:ss} ExchangeTimeUtc={ExchangeTimeUtc:O} ReceivedAtUtc={ReceivedAtUtc:O}",
                    tick.SymbolToken, minute, exchangeUtc, tick.ReceivedAtUtc);
            }
            else
            {
                _logger.LogDebug(
                    "Ignoring tick older than candle retention. Token={Token} CandleTimeIST={CandleTime:yyyy-MM-dd HH:mm:ss} ExchangeTimeUtc={ExchangeTimeUtc:O}",
                    tick.SymbolToken, minute, exchangeUtc);
            }
        }
    }

    private void FinalizeDue(string token, State state, DateTime nowUtc)
    {
        foreach (var item in state.Pending.ToArray())
        {
            var candleEndIst = item.Key.AddMinutes(1);
            var candleEndUtc = TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candleEndIst, DateTimeKind.Unspecified), India);
            if (nowUtc < candleEndUtc + _finalizationDelay)
                continue;

            state.Pending.Remove(item.Key);
            state.Finalized[item.Key] = item.Value;
            UpsertMinute(state, item.Value.Candle);
            QueueWrite(token, "1m", item.Value.Candle, correction: false);
            RebuildRollups(token, state, item.Key, correction: false);
        }
    }

    private void RebuildRollups(string token, State state, DateTime changedMinute, bool correction)
    {
        BuildRollup(token, state, changedMinute, 5, "5m", correction);
        BuildRollup(token, state, changedMinute, 30, "30m", correction);
    }

    private void BuildRollup(string token, State state, DateTime changedMinute, int intervalMinutes, string interval, bool correction)
    {
        var start = new DateTime(
            changedMinute.Year, changedMinute.Month, changedMinute.Day,
            changedMinute.Hour, (changedMinute.Minute / intervalMinutes) * intervalMinutes, 0);
        var group = new List<Candle>(intervalMinutes);
        for (var i = 0; i < intervalMinutes; i++)
        {
            var timestamp = start.AddMinutes(i);
            var candle = state.Finalized.TryGetValue(timestamp, out var bucket)
                ? bucket.Candle
                : null;
            if (candle == null)
                return; // Only publish complete, contiguous intervals.
            group.Add(candle);
        }

        var aggregate = new Candle
        {
            Timestamp = start,
            Open = group[0].Open,
            High = group.Max(x => x.High),
            Low = group.Min(x => x.Low),
            Close = group[^1].Close,
            Volume = group.Sum(x => x.Volume)
        };
        QueueWrite(token, interval, aggregate, correction);
    }

    private void UpsertMinute(State state, Candle candle)
    {
        var index = state.Minute.FindIndex(x => x.Timestamp == candle.Timestamp);
        if (index >= 0)
            state.Minute[index] = candle;
        else
            state.Minute.Add(candle);
        state.Minute.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        if (state.Minute.Count > 500)
            state.Minute.RemoveRange(0, state.Minute.Count - 500);
    }

    private static void PruneFinalized(State state, DateTime nowIst)
    {
        var cutoff = nowIst - FinalizedRetention;
        foreach (var key in state.Finalized.Keys.Where(x => x < cutoff).ToArray())
            state.Finalized.Remove(key);
    }

    private void QueueWrite(string token, string interval, Candle candle, bool correction)
    {
        if (!_writes.Writer.TryWrite(new CandleWrite(token, interval, candle, correction)))
            _logger.LogError("Candle persistence queue rejected a candle. Token={Token} Interval={Interval} Timestamp={Timestamp}", token, interval, candle.Timestamp);
    }

    private async Task CandleWriterLoopAsync()
    {
        await foreach (var item in _writes.Reader.ReadAllAsync())
        {
            try
            {
                await UpsertFileAsync(item.Token, item.Interval, item.Candle, CancellationToken.None);
                _rawMarketData.RecordCandle(item.Token, "NSE", item.Interval, item.Candle);
                await _hub.Clients.All.SendAsync(
                    item.Correction ? "CandleCorrected" : "CandleClosed",
                    new { token = item.Token, interval = item.Interval, candle = item.Candle, isCorrection = item.Correction },
                    CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist/publish candle. Token={Token} Interval={Interval} Timestamp={Timestamp}", item.Token, item.Interval, item.Candle.Timestamp);
            }
        }
    }

    private async Task UpsertFileAsync(string token, string interval, Candle candle, CancellationToken cancellationToken)
    {
        var safeToken = string.Concat(token.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
        var path = Path.Combine(_directory, $"{safeToken}_{interval}.json");
        var temp = path + $".{Guid.NewGuid():N}.tmp";

        try
        {
            List<Candle> all = [];
            if (File.Exists(path))
            {
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true);
                all = await JsonSerializer.DeserializeAsync<List<Candle>>(input, _json, cancellationToken) ?? [];
            }

            var index = all.FindIndex(x => x.Timestamp == candle.Timestamp);
            if (index >= 0)
                all[index] = candle;
            else
                all.Add(candle);
            all = all.OrderBy(x => x.Timestamp).ToList();

            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
                await JsonSerializer.SerializeAsync(output, all, _json, cancellationToken);

            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static CandleBucket NewBucket(DateTime timestampIst, global::MarketTick tick, DateTime exchangeUtc)
    {
        var candle = new Candle
        {
            Timestamp = timestampIst,
            Open = tick.Ltp,
            High = tick.Ltp,
            Low = tick.Ltp,
            Close = tick.Ltp,
            Volume = Math.Max(0, tick.LastTradedQuantity)
        };
        return new CandleBucket
        {
            Candle = candle,
            FirstTickUtc = exchangeUtc,
            LastTickUtc = exchangeUtc,
            LastSequence = tick.SequenceNumber
        };
    }

    private static void Update(CandleBucket bucket, global::MarketTick tick, DateTime exchangeUtc)
    {
        var candle = bucket.Candle;
        candle.High = Math.Max(candle.High, tick.Ltp);
        candle.Low = Math.Min(candle.Low, tick.Ltp);
        candle.Volume += Math.Max(0, tick.LastTradedQuantity);

        if (exchangeUtc < bucket.FirstTickUtc)
        {
            bucket.FirstTickUtc = exchangeUtc;
            candle.Open = tick.Ltp;
        }

        if (exchangeUtc > bucket.LastTickUtc ||
            (exchangeUtc == bucket.LastTickUtc && tick.SequenceNumber >= bucket.LastSequence))
        {
            bucket.LastTickUtc = exchangeUtc;
            bucket.LastSequence = tick.SequenceNumber;
            candle.Close = tick.Ltp;
        }
    }

    private static DateTime NormalizeUtc(DateTime exchangeTime, DateTime receivedAtUtc)
    {
        var value = exchangeTime == default ? receivedAtUtc : exchangeTime;
        if (value == default) value = DateTime.UtcNow;
        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }

    private static DateTime FloorMinute(DateTime value) =>
        new(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, DateTimeKind.Unspecified);

    private static TimeZoneInfo ResolveIndiaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }

    public void Dispose()
    {
        _socket.TickReceived -= OnTick;
        _timerCts.Cancel();
        _timerCts.Dispose();
    }
}
