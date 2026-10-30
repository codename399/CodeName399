using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.WebSocketOrder;

public interface IAngelOneService
{
    Task<AngelOneLoginData> GetLoginDataAsync();

    Task<decimal> GetAvailableBalanceAsync();

    Task<List<Position>> GetPositionsAsync(bool forceRefresh = false);
    bool LastPositionRefreshSucceeded { get; }
    bool ValidateRegisteredStaticIp(out string error);

    Task<List<OrderBook>> GetOrderBookAsync();

    Task<PlaceOrderResponse> PlaceOrderAsync(PlaceOrderRequest request, bool forceLive = false);

    Task<bool> SquareOffAsync(string symbol, int quantity, TradeDirection direction = TradeDirection.Long);
    Task<bool> SquareOffAsync(string exchange, string symbolToken, int quantity, TradeDirection direction = TradeDirection.Long);

    Task<bool> CancelOrderAsync(string orderId);

    Task<PlaceOrderResponse> CancelOrderAsync(string orderId, string variety);

    Task<decimal> GetRequiredMarginAsync(MarginRequest request);

    Task<decimal> GetRequiredMarginAsync(
        string symbolToken,
        int quantity,
        decimal price,
        string exchange = "NSE",
        string productType = "INTRADAY",
        string tradeType = "BUY",
        bool forceLive = false
    );

    Task<PlaceOrderResponse> ModifyOrderAsync(ModifyOrderRequest request);

    Task<BrokerageEstimateResponse> EstimateChargesAsync(
    BrokerageEstimateRequest request);

    Task RefreshPositionsAsync();

    Task<OrderData?> GetIndividualOrderAsync(string uniqueOrderId);
    Task<CodeName399.Kite.API.Models.ReferencePriceSnapshot?> GetReferencePriceAsync(string exchange, string symbolToken, string tradingSymbol, CancellationToken cancellationToken = default);
}
