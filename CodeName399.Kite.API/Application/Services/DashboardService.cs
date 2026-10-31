using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Services
{
    public class DashboardService
    {
        private readonly IAngelOneService _angel;

        protected readonly TradingSettingsProvider _tradingSettingsProvider;

        private readonly IMarketHoursService _marketHoursService;

        public DashboardService(
            IAngelOneService angel,
            TradingSettingsProvider tradingSettingsProvider,
            IMarketHoursService marketHoursService
        )
        {
            _angel = angel;
            _tradingSettingsProvider = tradingSettingsProvider;
            _marketHoursService = marketHoursService;
        }

        public async Task<DashboardResponse> GetDashboardAsync()
        {
            decimal balance = 0;

            try
            {
                balance = await _angel.GetAvailableBalanceAsync();
            }
            catch { }

            return new DashboardResponse
            {
                AvailableCash = balance,

                AutoTradingEnabled = _tradingSettingsProvider.Current.EnableAutoTrading,

                MarketStatus = _marketHoursService.IsOpen() ? "OPEN" : "CLOSED",
            };
        }
    }
}
