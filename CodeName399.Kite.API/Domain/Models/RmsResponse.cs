namespace CodeName399.Kite.API.Models
{
    public class RmsResponse
    {
        public RmsData Data { get; set; }
    }

    public class RmsData
    {
        public decimal AvailableCash
        {
            get;
            set;
        }
    }
}