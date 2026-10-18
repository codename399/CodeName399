using System.Collections.Concurrent;

namespace CodeName399.Kite.API.Services;

public class SymbolLockService
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(
        StringComparer.OrdinalIgnoreCase
    );

    public Task<bool> TryAcquirePositionAsync(string exchange, string symbolToken) => TryAcquireAsync($"{exchange}:{symbolToken}");

    public void ReleasePosition(string exchange, string symbolToken) => Release($"{exchange}:{symbolToken}");

    public void RestorePosition(string exchange, string symbolToken) => Restore($"{exchange}:{symbolToken}");

    public bool IsPositionLocked(string exchange, string symbolToken) => IsLocked($"{exchange}:{symbolToken}");

    public async Task<bool> TryAcquireAsync(string symbol)
    {
        symbol = Normalize(symbol);

        var semaphore = _locks.GetOrAdd(
            symbol,
            _ =>
            {
                var s = new SemaphoreSlim(1, 1);
                return s;
            }
        );

        return await semaphore.WaitAsync(0);
    }

    public void Restore(string symbol)
    {
        symbol = Normalize(symbol);

        _locks.GetOrAdd(
            symbol,
            _ =>
            {
                var s = new SemaphoreSlim(1, 1);
                s.Wait();
                return s;
            }
        );
    }

    public void Release(string symbol)
    {
        symbol = Normalize(symbol);

        if (_locks.TryRemove(symbol, out var semaphore))
        {
            semaphore.Release();
            semaphore.Dispose();
        }
    }

    public bool IsLocked(string symbol)
    {
        return _locks.ContainsKey(Normalize(symbol));
    }

    private static string Normalize(string symbol)
    {
        return symbol.Trim().ToUpperInvariant();
    }
}
