using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Indicator;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.BackgroundServices;

public class HistoricalCacheBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly HistoricalDownloadQueue _queue;
    private readonly HistoricalCacheService _cache;
    private readonly ILogger<HistoricalCacheBackgroundService> _logger;
    private readonly MarketClock _clock;
    private readonly TradingSettingsProvider _settings;

    private int _recoveryPerformed;

    public HistoricalCacheBackgroundService(
        IServiceProvider serviceProvider,
        HistoricalDownloadQueue queue,
        HistoricalCacheService cache,
        ILogger<HistoricalCacheBackgroundService> logger,
        MarketClock clock,
        TradingSettingsProvider settings
    )
    {
        _serviceProvider = serviceProvider;
        _queue = queue;
        _cache = cache;
        _logger = logger;
        _clock = clock;
        _settings = settings;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Restore interrupted downloads after a process crash. Instrument master
        // loading is external I/O and must never be allowed to terminate the host.
        if (Interlocked.Exchange(ref _recoveryPerformed, 1) == 0)
        {
            try
            {
                var pending = _cache.GetPendingTokens();
                var stale = _cache.GetStaleTokens(TimeSpan.FromMinutes(3));
                var activeInstrument = InstrumentType.Equity;

                using var recoveryScope = _serviceProvider.CreateScope();
                var instrumentService = recoveryScope.ServiceProvider.GetRequiredService<IInstrumentService>();
                var instruments = await instrumentService.LoadInstruments();
                var instrumentMap = instruments
                    .Where(x => !string.IsNullOrWhiteSpace(x.token))
                    .GroupBy(x => x.token, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.FirstOrDefault(x => IsHistoricalCompatibleInstrument(activeInstrument, x.InstrumentType)) ?? g.First(),
                        StringComparer.OrdinalIgnoreCase);

                var eligible = new List<string>();
                var skipped = 0;

                foreach (var token in pending.Concat(stale).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!instrumentMap.TryGetValue(token, out var instrument) || !IsHistoricalCompatibleInstrument(activeInstrument, instrument.InstrumentType))
                    {
                        skipped++;
                        continue;
                    }

                    eligible.Add(token);

                    const int priority = 1;
                    if (!_queue.IsQueued(token))
                        _queue.Enqueue(token, priority);
                }

                _logger.LogInformation(
                    "Historical cache recovery completed. ActiveInstrument={ActiveInstrument}, Ready={Ready}, Eligible={Eligible}, SkippedIncompatible={SkippedIncompatible}, Pending={Pending}, Stale={Stale}.",
                    activeInstrument,
                    _cache.GetReady().Count(),
                    eligible.Count,
                    skipped,
                    pending.Count,
                    stale.Count);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Historical cache startup recovery failed. The Kite host will remain running and recovery will be retried by normal queue processing."
                );
            }
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_queue.TryDequeue(out var token, out var tokenPriority))
                {
                    await Task.Delay(1000, stoppingToken);
                    continue;
                }

                using var flow = TradingFlowActivity.Start("trading.historical-download", symbolToken: token);
                _logger.LogDebug("FLOW START HistoricalDownload Token={Token}, QueueDepth={QueueDepth}",
                    token, _cache.GetPendingTokens().Count);

                using var scope = _serviceProvider.CreateScope();

                var historical =
                    scope.ServiceProvider.GetRequiredService<IHistoricalDataService>();

                var angel =
                    scope.ServiceProvider.GetRequiredService<IAngelOneService>();

                var watchList =
                    scope.ServiceProvider.GetRequiredService<WatchListService>();

                var instrumentService =
                    scope.ServiceProvider.GetRequiredService<IInstrumentService>();

                var login = await angel.GetLoginDataAsync();

                try
                {
                    stoppingToken.ThrowIfCancellationRequested();

                    // A ready cache only skips downloading when its last
                    // 1-minute candle is still fresh. Persisted stale caches
                    // must be incrementally refreshed after a restart.
                    if (_cache.TryGet(token, out var readyHistory))
                    {
                        var last = readyHistory.Candles1m.LastOrDefault()?.Timestamp;

                        if (last.HasValue && _clock.Now - last.Value <= TimeSpan.FromMinutes(3))
                        {
                            MarkWatchlistReady(watchList, token, readyHistory);
                            continue;
                        }
                    }

                    // Pending entries are intentionally allowed to be
                    // processed even if the symbol is temporarily absent
                    // from the watchlist. Historical cache is independent
                    // of the current discovery result.
                    _cache.AddPending(token);

                    var instrument = await instrumentService.GetByTokenAsync(token);
                    var exchange = instrument?.exch_seg ?? "NSE";

                    CachedHistoricalData? existing = null;

                    // TryGet only returns ready caches, which is exactly what
                    // we need here: a stale ready cache can be incrementally
                    // refreshed; a pending marker has no candles.
                    if (_cache.TryGet(token, out var readyExisting))
                        existing = readyExisting;

                    List<Candle> candles1m;

                    _logger.LogInformation(
                        "EMA DIAGNOSTIC HISTORICAL REQUEST Token={Token} Exchange={Exchange} ExistingReadyCache={ExistingReadyCache} ExistingOneMinuteCandles={ExistingOneMinuteCandles} RequestType={RequestType} RequestedInterval=ONE_MINUTE",
                        token, exchange, existing != null, existing?.Candles1m.Count ?? 0,
                        existing == null || existing.Candles1m.Count == 0 ? "FULL_DOWNLOAD" : "INCREMENTAL_REFRESH");

                    if (existing == null || existing.Candles1m.Count == 0)
                    {
                        candles1m = await historical.GetAsync(
                            token,
                            login.JwtToken,
                            "ONE_MINUTE",
                            exchange);
                    }
                    else
                    {
                        candles1m = await historical.RefreshAsync(
                            token,
                            login.JwtToken,
                            "ONE_MINUTE",
                            existing.Candles1m,
                            exchange);
                    }

                    candles1m ??= [];

                    var positiveCandles = candles1m.Where(x => x.Close > 0m).ToList();
                    _logger.LogInformation(
                        "EMA DIAGNOSTIC HISTORICAL RESPONSE Token={Token} Exchange={Exchange} CandleCount={CandleCount} PositiveCloseCount={PositiveCloseCount} FirstCandleIST={FirstCandleIST} LastCandleIST={LastCandleIST} FirstClose={FirstClose:F6} LastClose={LastClose:F6} RequiredOneMinuteCandles=21",
                        token, exchange, candles1m.Count, positiveCandles.Count,
                        candles1m.FirstOrDefault()?.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") ?? "none",
                        candles1m.LastOrDefault()?.Timestamp.ToString("yyyy-MM-dd HH:mm:ss") ?? "none",
                        candles1m.FirstOrDefault()?.Close ?? 0m,
                        candles1m.LastOrDefault()?.Close ?? 0m);

                    if (positiveCandles.Count < 21)
                    {
                        _logger.LogWarning(
                            "EMA DIAGNOSTIC CACHE NOT READY Token={Token} Reason=OneMinuteCandleCountBelowReadinessThreshold ActualCandles={ActualCandles} PositiveCloseCandles={PositiveCloseCandles} RequiredCandles=21 CacheExists={CacheExists} ExistingReadyCache={ExistingReadyCache} Action=Requeue",
                            token, candles1m.Count, positiveCandles.Count, _cache.Exists(token), existing != null);

                        Requeue(token, tokenPriority);
                        continue;
                    }

                    // EMA9/EMA21 is computed from one-minute closes. Do not block
                    // readiness on a separate five-minute candle minimum; sparse but
                    // valid one-minute history can still initialize the EMA strategy.
                    var candles5m = CandleBuilder.Build5Minute(candles1m);

                    var currentPrice = positiveCandles[^1].Close;

                    var history = new CachedHistoricalData
                    {
                        SymbolToken = token,
                        Exchange = exchange,

                        Candles1m = candles1m,
                        Candles5m = candles5m,
                        CurrentPrice = currentPrice,

                        PreviousDayVolume = 0,
                        Snapshot = null,

                        Last1MinuteRefresh = DateTime.UtcNow,
                        Last5MinuteRefresh = DateTime.UtcNow,
                        LastIndicatorUpdate = default,
                        LastPriceUpdate = DateTime.UtcNow,

                        IsReady = true
                    };

                    _cache.Set(history);

                    MarkWatchlistReady(watchList, token, history);

                    _logger.LogInformation(
                        "Historical cache ready for {Token}. 1m={OneMinute}, 5m={FiveMinute}.",
                        token,
                        candles1m.Count,
                        candles5m.Count);

                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "FLOW ERROR HistoricalDownload Token={Token}. Failed to build historical cache.",
                        token);

                    _cache.AddPending(token);

                    // A rate-limit exhaustion is a broker-wide condition.
                    // Requeueing the same token immediately creates a retry
                    // loop and starves the rest of the queue. The global
                    // HistoricalRateLimiter has already installed a cooldown;
                    // leave this token pending and let the next discovery/recovery
                    // cycle enqueue it again.
                    if (!ex.Message.Contains("Historical API exceeded retry limit", StringComparison.OrdinalIgnoreCase))
                    {
                        Requeue(token, tokenPriority);
                    }
                }

                await Task.Delay(
                    _queue.Count > 25
                        ? TimeSpan.FromMilliseconds(250)
                        : TimeSpan.FromMilliseconds(100),
                    stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Historical cache background service failed.");

                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    stoppingToken);
            }
        }
    }

    private static bool IsHistoricalCompatibleInstrument(InstrumentType active, InstrumentType candidate) => candidate == InstrumentType.Equity;

    private void Requeue(string token, int priority)
    {
        if (!_queue.IsQueued(token))
            _queue.Enqueue(token, Math.Max(priority, 1));
    }

    private static void MarkWatchlistReady(
        WatchListService watchList,
        string token,
        CachedHistoricalData history)
    {
        var stock = watchList.Get(token);

        if (stock == null)
            return;

        stock.HistoricalLoaded = true;

        if (stock.LastHistoricalUpdate == default)
        {
            stock.LastHistoricalUpdate =
                history.LastPriceUpdate != default
                    ? history.LastPriceUpdate
                    : DateTime.UtcNow;
        }
    }
}
