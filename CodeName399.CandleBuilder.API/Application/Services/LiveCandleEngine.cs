using System.Collections.Concurrent;
using System.Text.Json;
using CodeName399.CandleBuilder.API.Models.Indicator;
using CodeName399.CandleBuilder.API.WebSocket.Interface;
using Microsoft.AspNetCore.SignalR;
using CodeName399.CandleBuilder.API.SignalRHubs;

namespace CodeName399.CandleBuilder.API.Services;

/// <summary>Builds exchange-time candles from live ticks and closes them at minute boundaries.</summary>
public sealed class LiveCandleEngine : IHostedService, IDisposable
{
    private sealed class State
    {
        public Candle? Current;
        public readonly List<Candle> Minute = [];
    }

    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAngelWebSocketClient _socket;
    private readonly IHubContext<MarketHub> _hub;
    private readonly ILogger<LiveCandleEngine> _logger;
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly CancellationTokenSource _timerCts = new();
    private Task? _boundaryTask;
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    public LiveCandleEngine(IAngelWebSocketClient socket, IHubContext<MarketHub> hub, ILogger<LiveCandleEngine> logger)
    {
        _socket = socket;
        _hub = hub;
        _logger = logger;
        var configured = Environment.GetEnvironmentVariable("CODENAME399_CANDLE_STORAGE_PATH");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("CODENAME399_CANDLE_STORAGE_PATH must be configured.");
        _directory = Path.GetFullPath(configured);
        Directory.CreateDirectory(_directory);
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _socket.TickReceived += OnTick;
        _boundaryTask = RunBoundaryClockAsync(_timerCts.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _socket.TickReceived -= OnTick;
        _timerCts.Cancel();
        if (_boundaryTask is not null)
        {
            try { await _boundaryTask.WaitAsync(cancellationToken); }
            catch (OperationCanceledException) { }
        }
    }

    // Wake at the next exact minute boundary (India time), rather than one minute
    // after startup. This closes a candle even if no tick arrives in the next minute.
    private async Task RunBoundaryClockAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, India);
            var nextBoundary = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0)
                .AddMinutes(1);
            var delay = nextBoundary - now;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, cancellationToken);

            CloseExpiredCandles(nextBoundary);
        }
    }

    private void CloseExpiredCandles(DateTime boundary)
    {
        foreach (var pair in _states)
        {
            var token = pair.Key;
            var state = pair.Value;
            lock (state)
            {
                if (state.Current is null || state.Current.Timestamp >= boundary)
                    continue;

                CloseCurrentCandle(token, state);
            }
        }
    }

    private void OnTick(object? sender, global::MarketTick tick)
    {
        if (tick == null || string.IsNullOrWhiteSpace(tick.SymbolToken) || tick.Ltp <= 0)
            return;

        var exchangeTime = tick.ExchangeTime == default ? DateTime.UtcNow : tick.ExchangeTime;
        var time = exchangeTime.Kind == DateTimeKind.Utc
            ? TimeZoneInfo.ConvertTimeFromUtc(exchangeTime, India)
            : TimeZoneInfo.ConvertTime(exchangeTime, India);
        var minute = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        var state = _states.GetOrAdd(tick.SymbolToken, _ => new State());

        lock (state)
        {
            if (state.Current is null)
            {
                state.Current = NewCandle(minute, tick);
            }
            else if (minute > state.Current.Timestamp)
            {
                // Close the previous candle using its last observed tick. No synthetic
                // candles are generated for minutes in which this instrument had no ticks.
                CloseCurrentCandle(tick.SymbolToken, state);
                state.Current = NewCandle(minute, tick);
            }
            else if (minute == state.Current.Timestamp)
            {
                Update(state.Current, tick);
            }
            // Ignore out-of-order ticks belonging to an already closed minute.
        }

        _ = _hub.Clients.All.SendAsync("TickReceived", tick);
    }

    private void CloseCurrentCandle(string token, State state)
    {
        if (state.Current is not { } closed)
            return;

        state.Current = null;
        AppendAsync(token, "1m", closed).GetAwaiter().GetResult();
        state.Minute.Add(closed);
        if (state.Minute.Count > 500)
            state.Minute.RemoveRange(0, state.Minute.Count - 500);

        if (closed.Timestamp.Minute % 5 == 4)
            Rollup(token, state, 5, "5m");
        if (closed.Timestamp.Minute % 30 == 29)
            Rollup(token, state, 30, "30m");

        _ = _hub.Clients.All.SendAsync("CandleClosed", new { token, interval = "1m", candle = closed });
    }

    private static Candle NewCandle(DateTime timestamp, global::MarketTick tick) => new()
    {
        Timestamp = timestamp,
        Open = tick.Ltp,
        High = tick.Ltp,
        Low = tick.Ltp,
        Close = tick.Ltp,
        Volume = Math.Max(0, tick.LastTradedQuantity)
    };

    private static void Update(Candle candle, global::MarketTick tick)
    {
        candle.High = Math.Max(candle.High, tick.Ltp);
        candle.Low = Math.Min(candle.Low, tick.Ltp);
        candle.Close = tick.Ltp;
        candle.Volume += Math.Max(0, tick.LastTradedQuantity);
    }

    private void Rollup(string token, State state, int intervalMinutes, string interval)
    {
        var group = state.Minute.TakeLast(intervalMinutes).ToList();
        if (group.Count != intervalMinutes)
            return;

        var first = group[0].Timestamp;
        // Only aggregate a complete, contiguous interval aligned to the clock.
        if (first.Minute % intervalMinutes != 0 ||
            group.Where((c, i) => c.Timestamp != first.AddMinutes(i)).Any())
            return;

        var candle = new Candle
        {
            Timestamp = first,
            Open = group[0].Open,
            High = group.Max(x => x.High),
            Low = group.Min(x => x.Low),
            Close = group[^1].Close,
            Volume = group.Sum(x => x.Volume)
        };
        AppendAsync(token, interval, candle).GetAwaiter().GetResult();
        _ = _hub.Clients.All.SendAsync("CandleClosed", new { token, interval, candle });
    }

    private async Task AppendAsync(string token, string interval, Candle candle)
    {
        var path = Path.Combine(_directory, $"{token}_{interval}.json");
        try
        {
            List<Candle> all = [];
            if (File.Exists(path))
            {
                await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                all = await JsonSerializer.DeserializeAsync<List<Candle>>(input, _json) ?? [];
            }

            var existingIndex = all.FindIndex(x => x.Timestamp == candle.Timestamp);
            if (existingIndex >= 0)
                all[existingIndex] = candle;
            else
            {
                all.Add(candle);
                all = all.OrderBy(x => x.Timestamp).ToList();
            }

            var temp = path + ".tmp";
            await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(output, all, _json);
            File.Move(temp, path, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to persist {Interval} candle for token {Token}", interval, token);
        }
    }

    public void Dispose()
    {
        _socket.TickReceived -= OnTick;
        _timerCts.Cancel();
        _timerCts.Dispose();
    }
}
