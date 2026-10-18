using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Services.Interface;
using CodeName399.Kite.API.WebSocket.Interface;

namespace CodeName399.Kite.API.Services;

public class MarketDataProvider : IMarketDataProvider
{
    private readonly IAngelWebSocketClient _webSocket;

    private readonly LiveMarketCache _cache;

    public MarketDataProvider(IAngelWebSocketClient webSocket, LiveMarketCache cache)
    {
        _webSocket = webSocket;
        _cache = cache;
    }

    public async Task StartAsync()
    {
        await _webSocket.StartAsync();
    }

    public async Task StopAsync()
    {
        await _webSocket.StopAsync();
    }

    public MarketTick? Get(string symbolToken)
    {
        return _cache.Get(symbolToken);
    }

    public MarketTick? GetFresh(string symbolToken, TimeSpan maxAge)
    {
        return _cache.TryGetFresh(symbolToken, maxAge, out var tick) ? tick : null;
    }

    public IReadOnlyCollection<MarketTick> GetAll()
    {
        return _cache.GetAll();
    }

    public async Task SubscribeAsync(IEnumerable<string> symbolTokens)
    {
        await _webSocket.SubscribeAsync(symbolTokens);
    }

    public async Task SubscribeAsync(IEnumerable<string> symbolTokens, string exchange)
    {
        await _webSocket.SubscribeAsync(symbolTokens, exchange);
    }

    public async Task UnsubscribeAsync(IEnumerable<string> symbolTokens)
    {
        await _webSocket.UnsubscribeAsync(symbolTokens);
    }

    public async Task UnsubscribeAsync(IEnumerable<string> symbolTokens, string exchange)
    {
        await _webSocket.UnsubscribeAsync(symbolTokens, exchange);
    }

    public async Task SetSubscriptionsAsync(IEnumerable<string> symbolTokens)
    {
        await _webSocket.SetSubscriptionsAsync(symbolTokens);
    }

    public async Task SetSubscriptionsAsync(IEnumerable<MarketSubscription> subscriptions)
    {
        await _webSocket.SetSubscriptionsAsync(subscriptions);
    }
}
