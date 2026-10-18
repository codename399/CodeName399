using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models;

public class MarginRequest
{
    [JsonPropertyName("positions")]
    public List<MarginPosition> Positions { get; set; } = [];
}
