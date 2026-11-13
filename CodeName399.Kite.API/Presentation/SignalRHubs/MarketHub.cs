using CodeName399.Kite.API.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace CodeName399.Kite.API.SignalRHubs;

public class MarketHub : Hub
{

    public MarketHub()
    {
    }

    public override async Task OnConnectedAsync()
    {
        await base.OnConnectedAsync();
    }
}
