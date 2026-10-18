namespace CodeName399.Kite.API.Models
{
    public class ScannerQuotes
    {
        public List<MarketQuoteData> Quotes { get; set; } = [];

        public List<MarketQuoteData> OwnedQuotes { get; set; } = [];
    }
}