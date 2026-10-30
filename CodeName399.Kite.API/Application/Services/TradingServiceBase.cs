using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Models.WebSocketOrder;
using CodeName399.Kite.API.Services.Brokerage;
using CodeName399.Kite.API.WebSocket.Interface;

namespace CodeName399.Kite.API.Services
{
    public abstract class TradingServiceBase
    {
        protected readonly IAngelOneService _angel;
        protected readonly TradingSettingsProvider _tradingSettingsProvider;
        protected readonly TradingStateService _state;
        protected readonly TradeManager _tradeManager;
        protected readonly ITradeOrderFileRepository _tradeHistoryRepository;
        protected readonly IBrokerageCalculatorService _brokerage;
        protected readonly OrderStatusCache _orderCache;
        protected readonly IAngelOrderWebSocket _orderWebSocket;
        protected readonly TradeValidationService _tradeValidation;
        protected readonly WatchListService _watchList;
        protected readonly ILogger _logger;

        protected readonly SymbolLockService _symbolLocks;

        protected TradingServiceBase(
            IAngelOneService angel,
            TradingSettingsProvider tradingSettingsProvider,
            TradingStateService state,
            TradeManager tradeManager,
            ITradeOrderFileRepository tradeHistoryRepository,
            IBrokerageCalculatorService brokerage,
            OrderStatusCache orderCache,
            IAngelOrderWebSocket orderWebSocket,
            TradeValidationService tradeValidation,
            WatchListService watchListService,
            SymbolLockService symbolLocks,
            ILogger logger
        )
        {
            _angel = angel;
            _tradingSettingsProvider = tradingSettingsProvider;
            _state = state;
            _tradeManager = tradeManager;
            _tradeHistoryRepository = tradeHistoryRepository;
            _brokerage = brokerage;
            _orderCache = orderCache;
            _orderWebSocket = orderWebSocket;
            _tradeValidation = tradeValidation;
            _watchList = watchListService;
            _symbolLocks = symbolLocks;
            _logger = logger;
        }

        protected async void OnOrderUpdated(OrderStatusMessage order)
        {
            if (order == null)
                return;

            using var flow = TradingFlowActivity.Start(
                "trading.order-update", order.Symbol, order.SymbolToken);

            _logger.LogDebug("FLOW START OrderUpdate Symbol={Symbol}, Token={Token}, OrderId={OrderId}, Transaction={Transaction}, Status={Status}, ExecutionState={ExecutionState}, Filled={Filled}/{Quantity}",
                order.Symbol, order.SymbolToken, order.OrderId, order.TransactionType,
                order.Status, order.ExecutionState, order.FilledQuantity, order.Quantity);

            try
            {
                await SyncTradeOrder(order);
                _logger.LogDebug("FLOW END OrderUpdate Symbol={Symbol}, Token={Token}, OrderId={OrderId}",
                    order.Symbol, order.SymbolToken, order.OrderId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed processing websocket order {OrderId}", order.OrderId);
            }
        }

        protected abstract Task SyncTradeOrder(OrderStatusMessage order);

        protected bool CanExecute()
        {
            var settings = _tradingSettingsProvider.Current;

            return (settings.EnableAutoTrading || settings.PaperTrading) && !_state.KillSwitch;
        }

        public virtual void Dispose()
        {
            _orderWebSocket.OrderUpdated -= OnOrderUpdated;
        }
    }
}
