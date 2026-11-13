namespace CodeName399.CandleBuilder.API.Constants
{
    public class AngelApiUrlConstant
    {
        public const string baseUrl = "https://apiconnect.angelone.in";
        public const string loginUrl = "/rest/auth/angelbroking/user/v1/loginByPassword";
        public const string marketDataUrl =
            "https://apiconnect.angelone.in/rest/secure/angelbroking/market/v1/quote/";
        public const string instrumentUrl =
            "https://margincalculator.angelbroking.com/OpenAPI_File/files/OpenAPIScripMaster.json";
        public const string socketUrl = "wss://smartapisocket.angelone.in/smart-stream";
        public const string generateTokenUrl =
            "https://apiconnect.angelone.in/rest/auth/angelbroking/jwt/v1/generateTokens";
    }
}
