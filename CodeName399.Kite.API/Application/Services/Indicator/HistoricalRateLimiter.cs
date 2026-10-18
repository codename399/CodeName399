using CodeName399.Kite.API.Helpers;
using System.Net;
using System.Threading.RateLimiting;

namespace CodeName399.Kite.API.Services.Indicator;

public sealed class HistoricalRateLimiter
{
    private readonly RateLimiter _perSecondLimiter;
    private readonly RateLimiter _perMinuteLimiter;
    private readonly ILogger<HistoricalRateLimiter> _logger;
    private long _cooldownUntilTicks;

    public HistoricalRateLimiter(ILogger<HistoricalRateLimiter> logger)
    {
        _logger = logger;

        // Angel One limit = 3 req/sec
        // Keep some safety margin.
        _perSecondLimiter = new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 1,
                Window = TimeSpan.FromSeconds(1),
                QueueLimit = 5000,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });

        // Keep a large safety margin below broker-side rolling limits. Fixed-window limits can burst at boundaries, so 60/min is safer than the nominal maximum.
        _perMinuteLimiter = new FixedWindowRateLimiter(
            new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 5000,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                AutoReplenishment = true
            });
    }

    public async Task WaitAsync(CancellationToken cancellationToken = default)
    {
        var cooldownTicks = Interlocked.Read(ref _cooldownUntilTicks);
        if (cooldownTicks > DateTime.UtcNow.Ticks)
        {
            var delay = new TimeSpan(cooldownTicks - DateTime.UtcNow.Ticks);
            if (delay > TimeSpan.Zero)
            {
                _logger.LogDebug("Historical API global cooldown active for {Seconds:F1}s.", delay.TotalSeconds);
                await Task.Delay(delay, cancellationToken);
            }
        }

        while (true)
        {
            using var lease = await _perSecondLimiter.AcquireAsync(1, cancellationToken);
            if (lease.IsAcquired)
                break;

            _logger.LogDebug("Historical API second limiter queue is saturated; retrying acquisition.");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        while (true)
        {
            using var lease = await _perMinuteLimiter.AcquireAsync(1, cancellationToken);
            if (lease.IsAcquired)
                break;

            _logger.LogDebug("Historical API minute limiter queue is saturated; retrying acquisition.");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    public void ReportRateLimit(TimeSpan? cooldown = null)
    {
        var until = DateTime.UtcNow.Add(cooldown ?? TimeSpan.FromSeconds(30)).Ticks;
        long observed;
        do
        {
            observed = Interlocked.Read(ref _cooldownUntilTicks);
            if (observed >= until)
                return;
        } while (Interlocked.CompareExchange(ref _cooldownUntilTicks, until, observed) != observed);
    }

    public async Task<T> ExecuteAsync<T>(
        Func<Task<T>> action,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        const int maxRetries = 5;

        // A rate-limit response is a server-side signal that this client should
        // slow down. Re-enter the central limiter before every retry instead of
        // sleeping and immediately sending another request outside the limiter.
        var delay = TimeSpan.FromSeconds(3);

        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            await WaitAsync(cancellationToken);

            try
            {
                return await action();
            }
            catch (AngelApiException ex)
                when (ex.StatusCode == HttpStatusCode.Forbidden ||
                      ex.Response.Contains("exceeding access rate", StringComparison.OrdinalIgnoreCase))
            {
                ReportRateLimit(TimeSpan.FromSeconds(30));

                if (attempt == maxRetries)
                    break;

                logger.LogWarning(
                    "Historical API rate limited. Attempt {Attempt}/{Max}. Backing off {Delay}s before re-entering the limiter.",
                    attempt,
                    maxRetries,
                    delay.TotalSeconds);

                await Task.Delay(delay, cancellationToken);

                // Cap the exponential backoff so one bad request does not
                // stall the entire historical queue indefinitely.
                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        throw new InvalidOperationException(
            "Historical API exceeded retry limit.");
    }
}