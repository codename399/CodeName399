using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models.WebSocketOrder;

public class OrderStatusResponse
{
    [JsonPropertyName("user-id")]
    public string UserId { get; set; } = "";

    [JsonPropertyName("status-code")]
    public string StatusCode { get; set; } = "";

    [JsonPropertyName("order-status")]
    public string OrderStatusCode { get; set; } = "";

    [JsonPropertyName("error-message")]
    public string ErrorMessage { get; set; } = "";

    [JsonPropertyName("orderData")]
    public OrderData? OrderData { get; set; }
}