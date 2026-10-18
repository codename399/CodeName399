using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Services;
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
    private readonly ILogger<MarketDiscoveryBackgroundService> _logger;
    private readonly IHubContext<MarketHub> _marketHub;

    private InstrumentType? _lastActiveInstrumentType;

    public MarketDiscoveryBackgroundService(
        IServiceProvider serviceProvider,
        WatchListService watchList,
        IMarketDataProvider marketDataProvider,
        TradingSettingsProvider settings,
        ILogger<MarketDiscoveryBackgroundService> logger,
        IHubContext<MarketHub> marketHub
    )
    {
        _serviceProvider = serviceProvider;
        _watchList = watchList;
        _marketDataProvider = marketDataProvider;
        _settings = settings;
        _logger = logger;
        _marketHub = marketHub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var config = _settings.Current;
                // Discovery and quote refresh run independently of market hours.
                // Market-hours enforcement belongs only to explicit buy/sell endpoints.

                // ---------------------------------------------------------
                // MARKET OPEN
                // ---------------------------------------------------------
                using var scope = _serviceProvider.CreateScope();

                var activeInstrumentType = InstrumentType.Equity;
                if (_lastActiveInstrumentType.HasValue &&
                    _lastActiveInstrumentType.Value != activeInstrumentType)
                {
                    var removed = _watchList.ClearNonOwnedForInstrumentType(activeInstrumentType);
                    await _marketDataProvider.SetSubscriptionsAsync(
                        _watchList.Current
                            .Where(ShouldSubscribe)
                            .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange))
                            .ToList());

                    _logger.LogDebug("FLOW STAGE InstrumentSwitch Previous={Previous}, Current={Current}, RemovedCandidates={RemovedCandidates}",
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
        // the watchlist, subscriptions, or UI. Owned positions
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

            // A successful quote response with no positive-change candidates is
            // different from a broker failure (which throws before this point).
            // Clear ordinary discovery candidates but retain open positions and
            // manual subscriptions.
            _watchList.ClearNonOwnedForInstrumentType(activeInstrumentType);
            await _marketDataProvider.SetSubscriptionsAsync(
                _watchList.Current
                    .Where(ShouldSubscribe)
                    .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange))
                    .ToList());

            await PublishWatchlistAsync();
            return;
        }

        // =========================================================
        // Update watchlist
        // =========================================================

        _watchList.UpdateForInstrumentType(stockList, activeInstrumentType);

        // Historical candles are loaded on demand by the candle endpoint only.

        // =========================================================
        // Build desired subscription set
        // =========================================================
        //
        // THIS IS THE P0 FIX.
        //
        // The selected equity universe and manually subscribed stocks are
        // subscribed independently of candle-history availability.
        // =========================================================

        var subscriptions = _watchList.Current
            .Where(ShouldSubscribe)
            .OrderByDescending(x => x.IsOwned)
            .ThenByDescending(x => x.Priority)
            .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange))
            .ToList();
        var desiredTokens = subscriptions.Select(x => x.SymbolToken).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

        await _marketDataProvider.SetSubscriptionsAsync(subscriptions);

        var openPositions = stockList.Count(x => x.IsOwned);

        var unownedCandidates = stockList.Count(x => !x.IsOwned);

        _logger.LogDebug("FLOW STAGE MarketDiscovery.Subscriptions Source={Source}, DesiredSubscriptions={DesiredSubscriptions}, OpenPositions={OpenPositions}, UnownedCandidates={UnownedCandidates}",
            source, desiredTokens.Count, openPositions, unownedCandidates);

        // Discovery is authoritative for the dashboard snapshot. Publish after
        // every successful discovery cycle.
        await PublishWatchlistAsync();

        _logger.LogDebug("FLOW END MarketDiscovery Source={Source}, WatchlistCount={WatchlistCount}",
            source, _watchList.Current.Count);
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
            .ThenByDescending(x => x.PullbackDetected)
            .ThenByDescending(x => x.IsManuallySubscribed)
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

    private static bool ShouldSubscribe(Gainer stock)
    {
        // Subscribe the selected top-ten equity universe immediately; the chart
        // creates new candles from ticks after its explicit history request.
        return stock.InstrumentType == InstrumentType.Equity
            && !string.IsNullOrWhiteSpace(stock.SymbolToken)
            && (stock.IsOwned || stock.IsManuallySubscribed || !stock.IsManuallyUnsubscribed);
    }

}
