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
    private readonly HistoricalDownloadQueue _historyQueue;
    private readonly ILogger<MarketDiscoveryBackgroundService> _logger;
    private readonly MarketClock _clock;
    private readonly IHubContext<MarketHub> _marketHub;

    private DateOnly? _historyPreloadedDate;

    public MarketDiscoveryBackgroundService(
        IServiceProvider serviceProvider,
        WatchListService watchList,
        IMarketDataProvider marketDataProvider,
        TradingSettingsProvider settings,
        HistoricalCacheService historyCache,
        HistoricalDownloadQueue historyQueue,
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

                // Discovery is deliberately independent of market-hours checks so
                // the UI can receive the latest available top-10 equity shortlist
                // even before/after the session. Entry and exit workers enforce
                // their own market-hours rules.
                var preloadTime = config.MarketOpenTime.Subtract(TimeSpan.FromMinutes(30));
                var isWeekday = now.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday;
                var isPreMarket = isWeekday
                    && now.TimeOfDay >= preloadTime
                    && now.TimeOfDay < config.MarketOpenTime;

                if (isPreMarket && _historyPreloadedDate != today)
                {
                    await PreloadHistoricalDataAsync(stoppingToken);
                    _historyPreloadedDate = today;
                }

                using var scope = _serviceProvider.CreateScope();
                var marketService = scope.ServiceProvider.GetRequiredService<IAngelMarketService>();
                var topStocks = await marketService.GetTopGainersAsync();
                await ProcessStocksAsync(topStocks, "top-10 equity discovery", stoppingToken);

                await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, config.WatchListRefreshMinutes)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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

        const int maxCandidates = 10;
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
                    .Where(x => !x.IsOwned && x.CurrentPrice > 0m && x.CurrentPrice <= _settings.Current.MaximumStockPrice)
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

    private async Task ProcessStocksAsync(
        IEnumerable<Gainer>? stocks,
        string source,
        CancellationToken cancellationToken
    )
    {
        using var flow = TradingFlowActivity.Start("trading.market-discovery", symbol: source);
        _logger.LogDebug("FLOW START MarketDiscovery Source={Source}, StockCount={StockCount}",
            source, stocks is ICollection<Gainer> collection ? collection.Count : -1);

        cancellationToken.ThrowIfCancellationRequested();

        var activeInstrumentType = InstrumentType.Equity;

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
        const int maxCandidates = 10;
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
                    .Where(x => !x.IsOwned && x.CurrentPrice > 0m && x.CurrentPrice <= _settings.Current.MaximumStockPrice)
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
                "Market discovery returned no candidates for active InstrumentType={InstrumentType}. Source={Source}. Preserving the last known watchlist.",
                activeInstrumentType, source);

            // Preserve the last known shortlist on a transient empty broker response.
            // Keep its subscriptions so live ticks can continue updating the UI.
            try
            {
                await _marketDataProvider.SetSubscriptionsAsync(
                    _watchList.Current
                        .Where(x => x.IsOwned || x.InstrumentType == activeInstrumentType)
                        .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange))
                        .ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh subscriptions after an empty discovery response; preserving the current UI shortlist.");
            }

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

        // Build the desired subscription set. All shortlisted candidates are
        // subscribed immediately; historical readiness does not gate market data.

        // Subscribe all ten shortlisted equities immediately. Historical candles
        // seed the EMA when available; otherwise live candles warm up the strategy.
        var desiredStocks = _watchList.Current
            .Where(x => x.IsOwned || x.InstrumentType == activeInstrumentType)
            .OrderByDescending(x => x.IsOwned)
            .ThenByDescending(x => x.Priority)
            .ToList();
        var subscriptions = desiredStocks
            .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange))
            .ToList();
        var desiredTokens = subscriptions.Select(x => x.SymbolToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var stock in desiredStocks)
            stock.IsSubscribed = false;

        try
        {
            await _marketDataProvider.SetSubscriptionsAsync(subscriptions);
            foreach (var stock in desiredStocks)
                stock.IsSubscribed = desiredTokens.Contains(stock.SymbolToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to apply the top-ten market-data subscriptions. The shortlist will still be published to the UI.");
        }

        var openPositions = desiredStocks.Count(x => x.IsOwned);

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

}
