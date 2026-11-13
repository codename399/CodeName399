using System.Collections.Concurrent;
using System.Text.Json;
using CodeName399.CandleBuilder.API.Models.Indicator;
using CodeName399.CandleBuilder.API.WebSocket.Interface;
using Microsoft.AspNetCore.SignalR;
using CodeName399.CandleBuilder.API.SignalRHubs;

namespace CodeName399.CandleBuilder.API.Services;

/// <summary>Builds exchange-time 1m candles from live ticks and rolls them into 5m/30m files.</summary>
public sealed class LiveCandleEngine : IHostedService, IDisposable
{
    private sealed class State { public Candle? Current; public readonly List<Candle> Minute = []; }
    private readonly ConcurrentDictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IAngelWebSocketClient _socket;
    private readonly IHubContext<MarketHub> _hub;
    private readonly ILogger<LiveCandleEngine> _logger;
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private static readonly TimeZoneInfo India = TimeZoneInfo.FindSystemTimeZoneById(OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    public LiveCandleEngine(IAngelWebSocketClient socket, IHubContext<MarketHub> hub, ILogger<LiveCandleEngine> logger)
    {
        _socket = socket; _hub = hub; _logger = logger;
        var configured = Environment.GetEnvironmentVariable("CODENAME399_CANDLE_STORAGE_PATH");
        if (string.IsNullOrWhiteSpace(configured)) throw new InvalidOperationException("CODENAME399_CANDLE_STORAGE_PATH must be configured.");
        _directory = Path.GetFullPath(configured); Directory.CreateDirectory(_directory);
    }

    public Task StartAsync(CancellationToken cancellationToken) { _socket.TickReceived += OnTick; return Task.CompletedTask; }
    public Task StopAsync(CancellationToken cancellationToken) { _socket.TickReceived -= OnTick; return Task.CompletedTask; }
    private void OnTick(object? sender, global::MarketTick tick)
    {
        if (tick == null || string.IsNullOrWhiteSpace(tick.SymbolToken) || tick.Ltp <= 0) return;
        var time = TimeZoneInfo.ConvertTime(tick.ExchangeTime == default ? DateTime.UtcNow : tick.ExchangeTime, India);
        var minute = new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, DateTimeKind.Unspecified);
        var state = _states.GetOrAdd(tick.SymbolToken, _ => new State());
        lock (state)
        {
            if (state.Current == null) state.Current = NewCandle(minute, tick);
            else if (minute > state.Current.Timestamp)
            {
                var closed = state.Current;
                AppendAsync(tick.SymbolToken, "1m", closed).GetAwaiter().GetResult();
                state.Minute.Add(closed);
                if (state.Minute.Count > 500) state.Minute.RemoveRange(0, state.Minute.Count - 500);
                if (closed.Timestamp.Minute % 5 == 4) Rollup(tick.SymbolToken, state, 5, "5m");
                if (closed.Timestamp.Minute % 30 == 29) Rollup(tick.SymbolToken, state, 30, "30m");
                _ = _hub.Clients.All.SendAsync("CandleClosed", new { token = tick.SymbolToken, interval = "1m", candle = closed });
                state.Current = NewCandle(minute, tick);
            }
            else Update(state.Current, tick);
        }
        _ = _hub.Clients.All.SendAsync("TickReceived", tick);
    }
    private static Candle NewCandle(DateTime t, global::MarketTick x) => new() { Timestamp=t, Open=x.Ltp, High=x.Ltp, Low=x.Ltp, Close=x.Ltp, Volume=Math.Max(0,x.LastTradedQuantity) };
    private static void Update(Candle c, global::MarketTick t) { c.High=Math.Max(c.High,t.Ltp); c.Low=Math.Min(c.Low,t.Ltp); c.Close=t.Ltp; c.Volume += Math.Max(0,t.LastTradedQuantity); }
    private void Rollup(string token, State state, int n, string interval)
    {
        var group = state.Minute.TakeLast(n).ToList(); if (group.Count != n) return;
        var first = group[0].Timestamp; if (group.Any(c => c.Timestamp < first || c.Timestamp >= first.AddMinutes(n))) return;
        var c = new Candle { Timestamp=first, Open=group[0].Open, High=group.Max(x=>x.High), Low=group.Min(x=>x.Low), Close=group[^1].Close, Volume=group.Sum(x=>x.Volume) };
        AppendAsync(token, interval, c).GetAwaiter().GetResult();
        _ = _hub.Clients.All.SendAsync("CandleClosed", new { token, interval, candle=c });
    }
    private async Task AppendAsync(string token, string interval, Candle candle)
    {
        var path=Path.Combine(_directory, $"{token}_{interval}.json");
        try
        {
            List<Candle> all=[];
            if (File.Exists(path)) { await using var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite); all=await JsonSerializer.DeserializeAsync<List<Candle>>(input,_json) ?? []; }
            if (all.Count == 0 || all[^1].Timestamp < candle.Timestamp) all.Add(candle);
            else if (all.Any(x=>x.Timestamp==candle.Timestamp)) { var idx=all.FindIndex(x=>x.Timestamp==candle.Timestamp); all[idx]=candle; }
            var temp=path+".tmp"; await using(var output=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)) await JsonSerializer.SerializeAsync(output,all,_json);
            File.Move(temp,path,true);
        }
        catch(Exception ex) { _logger.LogError(ex,"Unable to persist {Interval} candle for token {Token}",interval,token); }
    }
    public void Dispose() { _socket.TickReceived -= OnTick; }
}
