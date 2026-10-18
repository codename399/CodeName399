using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models
{
    public class PlaceOrderResponse
    {
        public bool Status { get; set; }

        public string Message { get; set; } = "";

        public PlaceOrderData Data { get; set; } = new();
    }

    public class PlaceOrderData
    {
        [JsonPropertyName("orderid")]
        public string OrderId { get; set; } = "";

        [JsonPropertyName("uniqueorderid")]
        public string UniqueOrderId { get; set; } = "";
    }
}
