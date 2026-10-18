namespace CodeName399.Kite.API.Constants
{
    public class AngelApiUrlConstant
    {
        public const string baseUrl = "https://apiconnect.angelone.in";
        public const string loginUrl = "/rest/auth/angelbroking/user/v1/loginByPassword";
        public const string marketDataUrl =
            "https://apiconnect.angelone.in/rest/secure/angelbroking/market/v1/quote/";
        public const string instrumentUrl =
            "https://margincalculator.angelbroking.com/OpenAPI_File/files/OpenAPIScripMaster.json";
        public const string generateTokenUrl =
            "https://apiconnect.angelone.in/rest/auth/angelbroking/jwt/v1/generateTokens";
        public const string historicalDataUrl =
            "/rest/secure/angelbroking/historical/v1/getCandleData";
        public const string positionUrl = "/rest/secure/angelbroking/order/v1/getPosition";
        public const string holdingsUrl = "/rest/secure/angelbroking/portfolio/v1/getHolding";
        public const string ltpDataUrl = "https://apiconnect.angelone.in/order-service/rest/secure/angelbroking/order/v1/getLtpData";
        public const string getRmsUrl = "/rest/secure/angelbroking/user/v1/getRMS";
        public const string placeOrderUrl = "/rest/secure/angelbroking/order/v1/placeOrder";
        public const string orderBookUrl = "/rest/secure/angelbroking/order/v1/getOrderBook";
        public const string tradeBookUrl = "/rest/secure/angelbroking/order/v1/getTradeBook";
        public const string cancelOrderUrl = "/rest/secure/angelbroking/order/v1/cancelOrder";
        public const string modifyOrderUrl = "/rest/secure/angelbroking/order/v1/modifyOrder";
        public const string marginCalculatorUrl = "/rest/secure/angelbroking/margin/v1/batch";
        public const string socketUrl = "wss://smartapisocket.angelone.in/smart-stream";
        public const string estimateCharges =
            "/rest/secure/angelbroking/brokerage/v1/estimateCharges";
        public const string individualOrderUrl =
    "/rest/secure/angelbroking/order/v1/details";
        public const string gttCreateRuleUrl = "/rest/secure/angelbroking/gtt/v1/createRule";
        public const string gttModifyRuleUrl = "/rest/secure/angelbroking/gtt/v1/modifyRule";
        public const string gttCancelRuleUrl = "/rest/secure/angelbroking/gtt/v1/cancelRule";
        public const string gttRuleDetailsUrl = "/rest/secure/angelbroking/gtt/v1/ruleDetails";
        public const string gttRuleListUrl = "/rest/secure/angelbroking/gtt/v1/ruleList";
    }
}
