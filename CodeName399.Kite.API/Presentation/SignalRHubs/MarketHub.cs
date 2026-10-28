using CodeName399.Kite.API.Services;
using Microsoft.AspNetCore.SignalR;

namespace CodeName399.Kite.API.SignalRHubs
{
    public class MarketHub : Hub
    {
        private readonly WatchListService _watchList;

        public MarketHub(
            WatchListService watchList)
        {
            _watchList = watchList;
        }

        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();

            var gainers = _watchList
                .Current.OrderByDescending(x => x.IsOwned)
                .ThenByDescending(x => x.Priority)
                .ThenByDescending(x => x.ChangePercent)
                .ToList();

            await Clients.Caller.SendAsync("GainersUpdated", gainers);
        }
    }
}
