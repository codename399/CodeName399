using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models;

public class MarginResponse
{
    [JsonPropertyName("status")]
    public bool Status { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("errorcode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public MarginData? Data { get; set; }
}
