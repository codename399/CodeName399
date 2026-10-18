using System.Collections.Concurrent;

namespace CodeName399.Kite.API.Services;

public sealed class LiveMarketCache
{
    private readonly ConcurrentDictionary<string, MarketTick> _ticks = new();

    private readonly ILogger<LiveMarketCache> _logger;

    public LiveMarketCache(ILogger<LiveMarketCache> logger)
    {
        _logger = logger;
    }

    public void Update(MarketTick tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        _ticks.AddOrUpdate(
            tick.SymbolToken,
            tick,
            (_, existing) =>
            {
                // Ignore out-of-order packets. Exchange sequence numbers are
                // monotonically increasing for a token during a connection.
                if (existing.SequenceNumber > 0 &&
                    tick.SequenceNumber > 0 &&
                    tick.SequenceNumber < existing.SequenceNumber)
                {
                    return existing;
                }

                return tick;
            });
    }

    public bool TryGetFresh(string symbolToken, TimeSpan maxAge, out MarketTick tick)
    {
        if (!_ticks.TryGetValue(symbolToken, out tick!))
            return false;

        return DateTime.UtcNow - tick.LastUpdated <= maxAge;
    }

    public bool TryGet(string symbolToken, out MarketTick tick)
    {
        return _ticks.TryGetValue(symbolToken, out tick!);
    }

    public MarketTick? Get(string symbolToken)
    {
        _ticks.TryGetValue(symbolToken, out var tick);

        return tick;
    }

    public IReadOnlyCollection<MarketTick> GetAll()
    {
        return _ticks.Values.ToList();
    }

    public IEnumerable<string> GetTrackedSymbols()
    {
        return _ticks.Keys;
    }

    public bool Contains(string symbolToken)
    {
        return _ticks.ContainsKey(symbolToken);
    }

    public bool Remove(string symbolToken)
    {
        return _ticks.TryRemove(symbolToken, out _);
    }

    public void Clear()
    {
        _ticks.Clear();
    }

    public int Count => _ticks.Count;
}
