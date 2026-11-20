using CodeName399.Kite.API.Models;
namespace CodeName399.Kite.API.WebSocket.Interface;

public interface IAngelWebSocketClient
{
    bool IsConnected { get; }

    event EventHandler<MarketTick>? TickReceived;

    Task StartAsync(CancellationToken cancellationToken = default);

    Task StopAsync(CancellationToken cancellationToken = default);

    Task SubscribeAsync(IEnumerable<string> tokens);
    Task SubscribeAsync(IEnumerable<string> tokens, string exchange);

    Task UnsubscribeAsync(IEnumerable<string> tokens);
    Task UnsubscribeAsync(IEnumerable<string> tokens, string exchange);

    Task SetSubscriptionsAsync(IEnumerable<string> tokens);
    Task SetSubscriptionsAsync(IEnumerable<MarketSubscription> subscriptions);
}
