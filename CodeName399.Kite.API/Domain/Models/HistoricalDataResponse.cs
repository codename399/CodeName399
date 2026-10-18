using System.Text.Json;

namespace CodeName399.Kite.API.Models
{
    public class HistoricalDataResponse
    {
        public bool Status { get; set; }

        public string Message { get; set; } = "";

        public string ErrorCode { get; set; } = "";

        public List<List<JsonElement>> Data { get; set; }
            = [];
    }
}