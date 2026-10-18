using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Indicator;
using CodeName399.Kite.API.Services.Interface;
using CodeName399.Kite.API.SignalRHubs;
using Microsoft.AspNetCore.SignalR;

namespace CodeName399.Kite.API.BackgroundServices;

public class MarketDiscoveryBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly WatchListService _watchList;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly TradingSettingsProvider _settings;
    private readonly HistoricalCacheService _historyCache;
    private readonly IMarketHoursService _marketHoursService;
    private readonly HistoricalDownloadQueue _historyQueue;
    private readonly TradeManager _tradeManager;
    private readonly ILogger<MarketDiscoveryBackgroundService> _logger;
    private readonly MarketClock _clock;
    private readonly IHubContext<MarketHub> _marketHub;

    private DateOnly? _historyPreloadedDate;
    private InstrumentType? _lastActiveInstrumentType;

    public MarketDiscoveryBackgroundService(
        IServiceProvider serviceProvider,
        WatchListService watchList,
        IMarketDataProvider marketDataProvider,
        TradingSettingsProvider settings,
        HistoricalCacheService historyCache,
        HistoricalDownloadQueue historyQueue,
        IMarketHoursService marketHoursService,
        TradeManager tradeManager,
        ILogger<MarketDiscoveryBackgroundService> logger,
        MarketClock clock,
        IHubContext<MarketHub> marketHub
    )
    {
        _serviceProvider = serviceProvider;
        _watchList = watchList;
        _marketDataProvider = marketDataProvider;
        _settings = settings;
        _historyCache = historyCache;
        _historyQueue = historyQueue;
        _marketHoursService = marketHoursService;
        _tradeManager = tradeManager;
        _logger = logger;
        _clock = clock;
        _marketHub = marketHub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = _settings.Current;
                var now = _clock.Now;
                var today = DateOnly.FromDateTime(now);

                // ---------------------------------------------------------
                // CLOSED MARKET
                // ---------------------------------------------------------
                // Never run normal discovery while the market is closed.
                // Historical preload is allowed only in the 30-minute
                // pre-market window.
                // ---------------------------------------------------------
                if (!_marketHoursService.IsOpen())
                {
                    var preloadTime = config.MarketOpenTime.Subtract(TimeSpan.FromMinutes(30));

                    var isPreMarket =
                        now.TimeOfDay >= preloadTime
                        && now.TimeOfDay < config.MarketOpenTime
                        && now.DayOfWeek is not DayOfWeek.Saturday
                        && now.DayOfWeek is not DayOfWeek.Sunday;

                    if (isPreMarket && _historyPreloadedDate != today)
                    {
                        await PreloadHistoricalDataAsync(stoppingToken);
                        _historyPreloadedDate = today;
                    }
                    else if (!isPreMarket)
                    {
                        await ClearClosedMarketStateAsync();
                    }

                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

                    continue;
                }

                // ---------------------------------------------------------
                // MARKET OPEN
                // ---------------------------------------------------------
                using var scope = _serviceProvider.CreateScope();

                var activeInstrumentType = InstrumentType.Equity;
                if (_lastActiveInstrumentType.HasValue &&
                    _lastActiveInstrumentType.Value != activeInstrumentType)
                {
                    var removed = _watchList.ClearNonOwnedForInstrumentType(activeInstrumentType);
                    _historyQueue.Clear();
                    await _marketDataProvider.SetSubscriptionsAsync(
                        _watchList.Current
                            .Where(x => x.IsOwned)
                            .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange))
                            .ToList());

                    _logger.LogDebug("FLOW STAGE InstrumentSwitch Previous={Previous}, Current={Current}, RemovedCandidates={RemovedCandidates}, HistoricalQueueCleared=true",
                        _lastActiveInstrumentType.Value, activeInstrumentType, removed);
                }
                _lastActiveInstrumentType = activeInstrumentType;

                var marketService = scope.ServiceProvider.GetRequiredService<IAngelMarketService>();

                var gainers = await marketService.GetTopGainersAsync();

                await ProcessStocksAsync(gainers, "live discovery", stoppingToken);

                await Task.Delay(
                    TimeSpan.FromMinutes(config.WatchListRefreshMinutes),
                    stoppingToken
                );
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in MarketDiscoveryBackgroundService");

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        _logger.LogInformation("MarketDiscoveryBackgroundService stopped.");
    }

    private async Task PreloadHistoricalDataAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var market = scope.ServiceProvider.GetRequiredService<IAngelMarketService>();

        var stocks = await market.GetTopGainersAsync();

        var activeInstrumentType = InstrumentType.Equity;

        var maxCandidates = Math.Max(1, _settings.Current.MaxCandidates);
        var normalizedStocks = stocks
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SymbolToken))
            .Where(x => x.InstrumentType == activeInstrumentType || x.IsOwned)
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        var stockList = normalizedStocks
            .Where(x => x.IsOwned)
            .Concat(
                normalizedStocks
                    .Where(x => !x.IsOwned)
                    .OrderByDescending(x => x.Priority)
                    .ThenByDescending(x => x.ChangePercent)
                    .Take(maxCandidates))
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        // Core strategy requires historical data, so queue the selected equity universe.
        var preloadLimit = stockList.Count;
        var preloadList = stockList
            .Where(x => x.IsOwned)
            .Concat(stockList
                .Where(x => !x.IsOwned)
                .OrderByDescending(x => x.Priority)
                .Take(preloadLimit))
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();
        foreach (var stock in preloadList)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_historyCache.Exists(stock.SymbolToken))
                continue;

            if (_historyQueue.IsQueued(stock.SymbolToken))
                continue;

            _historyCache.AddPending(stock.SymbolToken);
            _historyQueue.Enqueue(stock.SymbolToken, Math.Max(stock.Priority, 1));
        }

        _logger.LogInformation(
            "Historical preload queued {Count} stocks from {UniverseCount}. "
                + "Watchlist and normal subscriptions unchanged.",
            preloadList.Count,
            stockList.Count
        );
    }

    private async Task ClearClosedMarketStateAsync()
    {
        var protectedTokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var trade in _tradeManager.GetOpen())
        {
            if (!string.IsNullOrWhiteSpace(trade.SymbolToken))
                protectedTokens.Add(trade.SymbolToken);
        }

        var removed = _watchList.RemoveNonActive(protectedTokens);

        // Outside market hours only active positions/active trades remain
        // subscribed. Normal discovery candidates are removed.
        await _marketDataProvider.SetSubscriptionsAsync(_tradeManager.GetOpen().Select(t => new MarketSubscription(t.SymbolToken, t.Exchange)).ToList());

        if (removed > 0)
        {
            _logger.LogInformation(
                "Market closed. Removed {Removed} normal watchlist stocks. "
                    + "Protected={Protected}.",
                removed,
                protectedTokens.Count
            );

            // The Angular client may still hold the last market-open snapshot.
            // Push the authoritative closed-market watchlist immediately so the
            // UI cannot continue displaying stale discovery candidates.
            await PublishClosedMarketWatchlistAsync();
        }
    }

    private async Task PublishClosedMarketWatchlistAsync()
    {
        var gainers = _watchList.Current
            .OrderByDescending(x => x.IsOwned)
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.ChangePercent)
            .ThenByDescending(x => x.ChangePercent)
            .ToList();

        await _marketHub.Clients.All.SendAsync("GainersUpdated", gainers);

        _logger.LogDebug("FLOW STAGE MarketClosedWatchlistPublished Remaining={Remaining}, Owned={Owned}, Protected={Protected}",
            gainers.Count,
            gainers.Count(x => x.IsOwned),
            gainers.Count(x => !x.IsOwned)
        );
    }

    private async Task ProcessStocksAsync(
        IEnumerable<Gainer>? stocks,
        string source,
        CancellationToken cancellationToken
    )
    {
        using var flow = TradingFlowActivity.Start("trading.market-discovery", symbol: source);
        _logger.LogDebug("FLOW START MarketDiscovery Source={Source}, StockCount={StockCount}, MarketOpen={MarketOpen}",
            source, stocks is ICollection<Gainer> collection ? collection.Count : -1, _marketHoursService.IsOpen());

        cancellationToken.ThrowIfCancellationRequested();

        // Capture the active instrument type for this discovery cycle.
        // This value must be local to ProcessStocksAsync; it is not a field.
        var activeInstrumentType = InstrumentType.Equity;

        // The active instrument is prepared before discovery in ExecuteAsync.
        // Keep this assignment defensive for direct/future callers.
        _lastActiveInstrumentType = activeInstrumentType;

        if (stocks == null)
        {
            _logger.LogWarning(
                "Market discovery returned null stocks. "
                    + "Source={Source}. Existing watchlist will be preserved.",
                source
            );

            return;
        }

        // Enforce the active instrument boundary before anything reaches
        // the watchlist, history queue, subscriptions, or UI. Owned positions
        // are preserved because they still require lifecycle management.
        var maxCandidates = Math.Max(1, _settings.Current.MaxCandidates);
        var normalizedStocks = stocks
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SymbolToken))
            .Where(x => x.InstrumentType == activeInstrumentType || x.IsOwned)
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        var stockList = normalizedStocks
            .Where(x => x.IsOwned)
            .Concat(
                normalizedStocks
                    .Where(x => !x.IsOwned)
                    .OrderByDescending(x => x.Priority)
                    .ThenByDescending(x => x.ChangePercent)
                    .Take(maxCandidates))
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToList();

        // =========================================================
        // Never destroy a valid watchlist because the broker returned
        // an empty response.
        // =========================================================

        if (stockList.Count == 0)
        {
            _logger.LogWarning(
                "Market discovery returned no candidates for active InstrumentType={InstrumentType}. Source={Source}. Non-owned watchlist entries are not restored.",
                activeInstrumentType, source);

            // On an instrument switch the previous universe has already been
            // removed.  Do not resurrect it merely because discovery returned
            // zero candidates.
            if (_lastActiveInstrumentType == activeInstrumentType)
            {
                _watchList.ClearNonOwnedForInstrumentType(activeInstrumentType);
            }

            await _marketDataProvider.SetSubscriptionsAsync(
                _watchList.Current
                    .Where(x => x.IsOwned)
                    .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange))
                    .ToList());

            await PublishWatchlistAsync();
            return;
        }

        // =========================================================
        // Update watchlist
        // =========================================================

        _watchList.UpdateForInstrumentType(stockList, activeInstrumentType);

        // =========================================================
        // Queue historical data
        // =========================================================

        var historicalCandidates = stockList
            .Where(x => !x.IsOwned && !_historyCache.Exists(x.SymbolToken))
            .OrderByDescending(x => x.Priority)
            .ThenByDescending(x => x.ChangePercent)
            .Take(stockList.Count)
            .ToList();

        foreach (var stock in historicalCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_historyQueue.IsQueued(stock.SymbolToken))
            {
                _historyCache.AddPending(stock.SymbolToken);
                var priority = Math.Max(stock.Priority, 1);
                _historyQueue.Enqueue(stock.SymbolToken, priority);
                _logger.LogDebug(
                    "Historical data queued for {Symbol} ({Token}) with priority {Priority}.",
                    stock.Symbol, stock.SymbolToken, priority);
            }
        }

        // =========================================================
        // Build desired subscription set
        // =========================================================
        //
        // THIS IS THE P0 FIX.
        //
        // HistoricalLoaded=false means the stock has not been
        // evaluated yet.
        //
        // It must NEVER be considered near-buy merely because a
        // placeholder signal says SETUP.
        //
        // Open positions are always subscribed.
        // =========================================================

        var subscriptions = stockList
            .Where(ShouldSubscribe)
            .OrderByDescending(x => x.IsOwned)
            .ThenByDescending(x => x.Priority)
            .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange))
            .ToList();
        var desiredTokens = subscriptions.Select(x => x.SymbolToken).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        await _marketDataProvider.SetSubscriptionsAsync(subscriptions);

        var openPositions = stockList.Count(x => x.IsOwned);

        var nearBuy = stockList.Count(x =>
            !x.IsOwned && string.Equals(x.Signal, "BUY", StringComparison.OrdinalIgnoreCase));

        var historicalLoading = stockList.Count(x => !x.HistoricalLoaded);

        _logger.LogDebug("FLOW STAGE MarketDiscovery.Subscriptions Source={Source}, DesiredSubscriptions={DesiredSubscriptions}, OpenPositions={OpenPositions}, NearBuy={NearBuy}, HistoricalLoading={HistoricalLoading}",
            source, desiredTokens.Count, openPositions, nearBuy, historicalLoading);

        // Discovery is authoritative for the dashboard snapshot. Publish after
        // every successful discovery cycle so a client that connected before
        // discovery does not have to wait for BuyTradingBackgroundService.
        await PublishWatchlistAsync();

        _logger.LogDebug("FLOW END MarketDiscovery Source={Source}, WatchlistCount={WatchlistCount}, HistoryPending={HistoryPending}",
            source, _watchList.Current.Count, _historyCache.GetPendingTokens().Count);
    }

    private async Task PublishWatchlistAsync()
    {
        var gainers = _watchList.Current
            .Select(x =>
            {
                // Final outbound guard: the dashboard must never receive an
                // equity SELL for a non-owned stock.
                if (x.InstrumentType == InstrumentType.Equity
                    && !x.IsOwned
                    && (string.Equals(x.Signal, "SELL", StringComparison.OrdinalIgnoreCase)
                        || x.Direction == TradeDirection.Short))
                {
                    x.Signal = "HOLD";
                    x.Direction = TradeDirection.Long;
                }

                return x;
            })
            .OrderByDescending(x => x.IsOwned)
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.ChangePercent)
            .ThenByDescending(x => x.ChangePercent)
            .ToList();

        try
        {
            await _marketHub.Clients.All.SendAsync("GainersUpdated", gainers);
        }
        catch (Exception ex)
        {
            // UI publication must never stop market discovery/subscriptions.
            _logger.LogWarning(ex, "Failed to publish market watchlist snapshot to SignalR.");
        }
    }

    private bool ShouldSubscribe(Gainer stock)
    {
        // Subscription is data acquisition, not entry eligibility. Once the
        // historical snapshot exists, subscribe to the equity so the Core
        // strategy can evaluate Recovery/PriceChange/Breakout directly.
        return stock.IsOwned || stock.HistoricalLoaded;
    }

}
