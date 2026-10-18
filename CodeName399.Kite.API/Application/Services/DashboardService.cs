using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Services
{
    public class DashboardService
    {
        private readonly IAngelOneService _angel;

        private readonly TradingStateService _state;

        protected readonly TradingSettingsProvider _tradingSettingsProvider;

        private readonly IMarketHoursService _marketHoursService;

        public DashboardService(
            IAngelOneService angel,
            TradingStateService state,
            TradingSettingsProvider tradingSettingsProvider,
            IMarketHoursService marketHoursService
        )
        {
            _angel = angel;
            _state = state;
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

                PaperTrading = _tradingSettingsProvider.Current.PaperTrading,

                DailyTrades = _state.DailyTrades,

                DailyLoss = _state.DailyLoss,

                KillSwitch = _state.KillSwitch,

                MarketStatus = _marketHoursService.IsOpen() ? "OPEN" : "CLOSED",

            };
        }
    }
}
