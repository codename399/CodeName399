using System.Text.Json.Serialization;

namespace CodeName399.Kite.API.Models;

public class MarginData
{
    [JsonPropertyName("totalMarginRequired")]
    public decimal TotalMarginRequired { get; set; }
}
