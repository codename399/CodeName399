using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Interface;

public class TradeInitializationHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<TradeInitializationHostedService> _logger;

    public TradeInitializationHostedService(
        IServiceProvider services,
        ILogger<TradeInitializationHostedService> logger
    )
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        using var scope = _services.CreateScope();

        var tradeManager =
            scope.ServiceProvider.GetRequiredService<TradeManager>();

        var reconciliation =
            scope.ServiceProvider.GetRequiredService<BrokerReconciliationService>();

        var marketData =
            scope.ServiceProvider.GetRequiredService<IMarketDataProvider>();

        try
        {
            await tradeManager.InitializeAsync();

            stoppingToken.ThrowIfCancellationRequested();

            // Reconcile broker positions before subscribing so that
            // only positions that are actually still open are treated
            // as recovered live positions.
            await reconciliation.ReconcileAsync();

            stoppingToken.ThrowIfCancellationRequested();

            // ---------------------------------------------------------
            // IMPORTANT:
            // Immediately subscribe all recovered/open positions.
            //
            // This does not depend on:
            //   - historical data
            //   - indicators
            //   - score
            //   - BUY/SETUP signal
            //
            // MarketDataProvider/AngelWebSocketClient stores the desired
            // subscription even if the WebSocket is not connected yet.
            // ---------------------------------------------------------

            var openPositionSubscriptions = tradeManager
                .GetOpen()
                .Where(x => !string.IsNullOrWhiteSpace(x.SymbolToken))
                .Select(x => new MarketSubscription(x.SymbolToken, string.IsNullOrWhiteSpace(x.Exchange) ? "NSE" : x.Exchange))
                .Distinct()
                .ToList();

            if (openPositionSubscriptions.Count > 0)
            {
                await marketData.SetSubscriptionsAsync(openPositionSubscriptions);

                _logger.LogInformation(
                    "Recovery: immediately subscribed to {Count} open positions.",
                    openPositionSubscriptions.Count);
            }
            else
            {
                _logger.LogInformation(
                    "Recovery: no open positions require immediate subscription.");
            }

            _logger.LogInformation(
                "Loaded {Count} active trades.",
                tradeManager.GetOpen().Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "Trade initialization cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogCritical(
                ex,
                "Trade initialization failed.");

            throw;
        }
    }
}
