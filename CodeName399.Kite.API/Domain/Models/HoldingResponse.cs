namespace CodeName399.Kite.API.Models
{
    public class HoldingsResponse
    {
        public bool Status { get; set; }

        public string Message { get; set; } = "";

        public string ErrorCode { get; set; } = "";

        public List<Holding> Data { get; set; }
            = [];
    }

    public class Holding
    {
        public string TradingSymbol
        {
            get;
            set;
        } = "";

        public int Quantity
        {
            get;
            set;
        }
    }
}