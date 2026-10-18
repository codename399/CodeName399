using CodeName399.Kite.API.Services.Indicator;
using System.Collections.Concurrent;

namespace CodeName399.Kite.API.Services;

public class HistoricalDownloadQueue
{
    private readonly object _lock = new();

    private readonly PriorityQueue<string, int> _queue = new();

    private readonly ConcurrentDictionary<string, int> _queued = new();

    public void Enqueue(string symbolToken, int priority)
    {
        if (string.IsNullOrWhiteSpace(symbolToken))
            return;

        lock (_lock)
        {
            if (_queued.TryAdd(symbolToken, priority))
            {
                // Higher priority values should be processed first,
                // therefore negate the value for PriorityQueue.
                _queue.Enqueue(symbolToken, -priority);
                return;
            }

            // Already queued with same or higher priority.
            if (
                _queued.TryGetValue(symbolToken, out var existingPriority)
                && existingPriority >= priority
            )
            {
                return;
            }

            // Upgrade priority.
            _queued[symbolToken] = priority;

            // Reinsert with higher priority.
            _queue.Enqueue(symbolToken, -priority);
        }
    }

    public bool TryDequeue(out string symbolToken)
        => TryDequeue(out symbolToken, out _);

    public bool TryDequeue(out string symbolToken, out int priority)
    {
        lock (_lock)
        {
            symbolToken = string.Empty;
            priority = 0;

            while (_queue.TryDequeue(out var token, out var queuedPriority))
            {
                if (!_queued.TryGetValue(token, out var currentPriority))
                    continue;

                // Ignore stale queue entries created before a priority upgrade.
                if (-queuedPriority != currentPriority)
                    continue;

                _queued.TryRemove(token, out _);

                symbolToken = token;
                priority = currentPriority;

                return true;
            }

            return false;
        }
    }

    public bool IsQueued(string symbolToken)
    {
        return _queued.ContainsKey(symbolToken);
    }

    public bool TryGetPriority(string symbolToken, out int priority)
    {
        return _queued.TryGetValue(symbolToken, out priority);
    }

    public void Clear()
    {
        lock (_lock)
        {
            _queued.Clear();

            while (_queue.TryDequeue(out _, out _)) { }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _queued.Count;
            }
        }
    }

    public bool IsQueuedOrCached(
    string symbolToken,
    HistoricalCacheService cache)
    {
        return _queued.ContainsKey(symbolToken) ||
               cache.Exists(symbolToken);
    }
}
