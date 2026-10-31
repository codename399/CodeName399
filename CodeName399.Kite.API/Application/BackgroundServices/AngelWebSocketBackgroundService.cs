using CodeName399.Kite.API.WebSocket.Interface;
using CodeName399.Kite.API.Application.Services;
using CodeName399.Kite.API.Models;

namespace CodeName399.Kite.API.BackgroundServices;

public class AngelWebSocketBackgroundService : BackgroundService
{
    private readonly IAngelWebSocketClient _webSocket;
    private readonly ILogger<AngelWebSocketBackgroundService> _logger;
    private readonly CommonService _commonService;
    private readonly MarketQuotePublisher _quotePublisher;

    public AngelWebSocketBackgroundService(
        IAngelWebSocketClient webSocket,
        CommonService commonService,
        MarketQuotePublisher quotePublisher,
        ILogger<AngelWebSocketBackgroundService> logger
    )
    {
        _webSocket = webSocket;
        _commonService = commonService;
        _quotePublisher = quotePublisher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_webSocket.IsConnected)
                {
                    await _webSocket.StartAsync(stoppingToken);

                    var ownedStocks = await _commonService.GetOwnedStocksAsync();
                    if (ownedStocks.Count > 0)
                    {
                        await _quotePublisher.SeedOwnedAsync(ownedStocks.Values);
                        var subscriptions = ownedStocks.Values
                            .Where(x => !string.IsNullOrWhiteSpace(x.SymbolToken))
                            .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange));
                        await _webSocket.SetSubscriptionsAsync(subscriptions);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "WebSocket disconnected.");

                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
    }
}
