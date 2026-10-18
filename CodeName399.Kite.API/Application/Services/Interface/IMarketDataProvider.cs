using CodeName399.Kite.API.Models;
namespace CodeName399.Kite.API.Services.Interface;

public interface IMarketDataProvider
{
    Task StartAsync();

    Task StopAsync();

    MarketTick? Get(string symbolToken);
    MarketTick? GetFresh(string symbolToken, TimeSpan maxAge);

    IReadOnlyCollection<MarketTick> GetAll();

    Task SubscribeAsync(
        IEnumerable<string> symbolTokens);
    Task SubscribeAsync(IEnumerable<string> symbolTokens, string exchange);

    Task UnsubscribeAsync(
        IEnumerable<string> symbolTokens);
    Task UnsubscribeAsync(IEnumerable<string> symbolTokens, string exchange);

    Task SetSubscriptionsAsync(
        IEnumerable<string> symbolTokens);

    Task SetSubscriptionsAsync(IEnumerable<MarketSubscription> subscriptions);
}
