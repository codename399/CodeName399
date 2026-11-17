using CodeName399.Kite.API.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace CodeName399.Kite.API.SignalRHubs;

public class MarketHub : Hub
{
    private readonly MarketQuotePublisher _quotes;

    public MarketHub(MarketQuotePublisher quotes)
    {
        _quotes = quotes;
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
        await Clients.Caller.SendAsync("QuotesUpdated", _quotes.GetSnapshot());
    }
}
