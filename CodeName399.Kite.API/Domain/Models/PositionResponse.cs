using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models;

public class PositionResponse
{
    [JsonPropertyName("status")]
    public bool Status { get; set; }

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("errorcode")]
    public string ErrorCode { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public List<Position> Data { get; set; } = [];
}