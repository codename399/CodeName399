using DatabaseRepository.Model;
using System.Text.Json.Serialization;

public class Instrument : Audit
{
    [JsonPropertyName("token")]
    public string token { get; set; } = "";

    [JsonPropertyName("symbol")]
    public string symbol { get; set; } = "";

    [JsonPropertyName("name")]
    public string name { get; set; } = "";

    [JsonPropertyName("exch_seg")]
    public string exch_seg { get; set; } = "";

    [JsonPropertyName("instrumenttype")]
    public string instrumenttype { get; set; } = "";

    [JsonPropertyName("is_cas_enabled")]
    public bool IsCasEnabled { get; set; }

    [JsonIgnore]
    public bool IsIntradayEligible { get; set; } = true;

    [JsonIgnore]
    public bool MarginVerified { get; set; }

    [JsonIgnore]
    public DateTime? MarginVerifiedOn { get; set; }

    [JsonIgnore]
    public CodeName399.Kite.API.Models.Enum.InstrumentType InstrumentType { get; set; }
}
