namespace CodeName399.Kite.API.Models
{
    public class OrderBookResponse
    {
        public bool Status
        {
            get;
            set;
        }

        public List<OrderBook> Data
        {
            get;
            set;
        } = [];
    }
}