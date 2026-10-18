using System.Collections.Concurrent;
using System.Threading.RateLimiting;

namespace CodeName399.Kite.API.Services;

/// <summary>
/// Central SmartAPI client-code rate limiter using the limits published in the
/// broker-documented SmartAPI rate limits.  Limits are intentionally
/// exact; callers should still batch requests where the API supports batching.
/// </summary>
public sealed class SmartApiRateLimiter
{
    private readonly ConcurrentDictionary<string, EndpointLimiter> _limiters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SmartApiRateLimiter> _logger;
    private long _globalCooldownUntilTicks;

    public SmartApiRateLimiter(ILogger<SmartApiRateLimiter> logger) => _logger = logger;

    public async Task WaitAsync(string api, CancellationToken cancellationToken = default)
    {
        await WaitForGlobalCooldownAsync(cancellationToken);
        var limiter = _limiters.GetOrAdd(api, CreateLimiter);
        await limiter.WaitAsync(cancellationToken);
    }

    public void ReportGlobalRateLimit(TimeSpan? cooldown = null)
    {
        var until = DateTime.UtcNow.Add(cooldown ?? TimeSpan.FromSeconds(15)).Ticks;
        long observed;
        do
        {
            observed = Interlocked.Read(ref _globalCooldownUntilTicks);
            if (observed >= until)
                return;
        } while (Interlocked.CompareExchange(ref _globalCooldownUntilTicks, until, observed) != observed);
    }

    private async Task WaitForGlobalCooldownAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var ticks = Interlocked.Read(ref _globalCooldownUntilTicks);
            var remaining = new TimeSpan(ticks - DateTime.UtcNow.Ticks);
            if (remaining <= TimeSpan.Zero)
                return;

            _logger.LogWarning(
                "SmartAPI global rate-limit cooldown active for {Seconds:F1}s.",
                remaining.TotalSeconds);
            await Task.Delay(remaining, cancellationToken);
        }
    }

    private EndpointLimiter CreateLimiter(string api)
    {
        var limits = api.ToLowerInvariant() switch
        {
            "login" => new[] { (1, TimeSpan.FromSeconds(1)) },
            "tokenrefresh" => new[] { (1, TimeSpan.FromSeconds(1)), (1000, TimeSpan.FromHours(1)) },
            "profile" => new[] { (3, TimeSpan.FromSeconds(1)), (1000, TimeSpan.FromHours(1)) },
            "rms" => new[] { (2, TimeSpan.FromSeconds(1)) },
            "order" or "placeorder" or "modifyorder" or "cancelorder" => new[]
            {
                (9, TimeSpan.FromSeconds(1)),
                (500, TimeSpan.FromMinutes(1)),
                (1000, TimeSpan.FromHours(1))
            },
            "getltpdata" or "ltpdata" => new[]
            {
                (10, TimeSpan.FromSeconds(1)),
                (500, TimeSpan.FromMinutes(1)),
                (5000, TimeSpan.FromHours(1))
            },
            "orderbook" or "positions" or "tradebook" or "searchscrip" => new[] { (1, TimeSpan.FromSeconds(1)) },
            "convertposition" => new[]
            {
                (10, TimeSpan.FromSeconds(1)),
                (500, TimeSpan.FromMinutes(1)),
                (5000, TimeSpan.FromHours(1))
            },
            "individualorder" => new[]
            {
                (10, TimeSpan.FromSeconds(1)),
                (500, TimeSpan.FromMinutes(1)),
                (5000, TimeSpan.FromHours(1))
            },
            "holdings" or "allholdings" => new[] { (1, TimeSpan.FromSeconds(1)) },
            "quote" => new[]
            {
                (10, TimeSpan.FromSeconds(1)),
                (500, TimeSpan.FromMinutes(1)),
                (5000, TimeSpan.FromHours(1))
            },
            "margin" => new[]
            {
                (10, TimeSpan.FromSeconds(1)),
                (500, TimeSpan.FromMinutes(1)),
                (5000, TimeSpan.FromHours(1))
            },
            "gttcreate" or "gttmodify" or "gttcancel" => new[]
            {
                (9, TimeSpan.FromSeconds(1)),
                (500, TimeSpan.FromMinutes(1)),
                (5000, TimeSpan.FromHours(1))
            },
            "gttdetails" or "gttlist" => new[]
            {
                (10, TimeSpan.FromSeconds(1)),
                (500, TimeSpan.FromMinutes(1)),
                (5000, TimeSpan.FromHours(1))
            },
            "optiongreek" or "gainerslosers" or "pcr" or "oibuildup" => new[] { (1, TimeSpan.FromSeconds(1)) },
            "historical" => new[]
            {
                (3, TimeSpan.FromSeconds(1)),
                (150, TimeSpan.FromMinutes(1)),
                (5000, TimeSpan.FromHours(1))
            },
            _ => new[] { (1, TimeSpan.FromSeconds(1)) }
        };

        return new EndpointLimiter(limits, _logger, api);
    }

    private sealed class EndpointLimiter
    {
        private readonly IReadOnlyList<RateLimiter> _limiters;
        private readonly ILogger _logger;
        private readonly string _api;

        public EndpointLimiter(IEnumerable<(int PermitLimit, TimeSpan Window)> limits, ILogger logger, string api)
        {
            _api = api;
            _logger = logger;
            _limiters = limits.Select(x => (RateLimiter)new FixedWindowRateLimiter(
                new FixedWindowRateLimiterOptions
                {
                    PermitLimit = x.PermitLimit,
                    Window = x.Window,
                    QueueLimit = 5000,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    AutoReplenishment = true
                })).ToList();
        }

        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            foreach (var limiter in _limiters)
            {
                while (true)
                {
                    using var lease = await limiter.AcquireAsync(1, cancellationToken);
                    if (lease.IsAcquired)
                        break;

                    _logger.LogWarning("SmartAPI rate limiter queue is saturated for {Api}; retrying acquisition.", _api);
                    await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
                }
            }
        }
    }
}
