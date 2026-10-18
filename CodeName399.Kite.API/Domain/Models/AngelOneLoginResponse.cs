using CodeName399.Kite.API.Helpers;
using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models
{
    public class AngelOneLoginResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("errorCode")]
        public string? ErrorCode { get; set; }

        [JsonPropertyName("data")]
        [JsonConverter(typeof(EmptyStringToNullConverter<AngelOneLoginData>))]
        public AngelOneLoginData? Data { get; set; }
    }

    public class AngelOneLoginData
    {
        [JsonPropertyName("jwtToken")]
        public string? JwtToken { get; set; }

        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("feedToken")]
        public string? FeedToken { get; set; }
    }
}