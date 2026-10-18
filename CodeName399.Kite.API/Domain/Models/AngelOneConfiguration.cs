namespace CodeName399.Kite.API.Models
{
    public class AngelOneConfiguration
    {
        public string UserId { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;

        public string ClientCode { get; set; } = string.Empty;

        public string Pin { get; set; } = string.Empty;

        public string TotpSecret { get; set; } = string.Empty;

        public string PublicIp { get; set; } = string.Empty;
        public string RegisteredStaticIp { get; set; } = string.Empty;
        public List<string> RegisteredStaticIps { get; set; } = [];
        public bool RequireRegisteredStaticIpForLiveTrading { get; set; } = true;
    }
}
