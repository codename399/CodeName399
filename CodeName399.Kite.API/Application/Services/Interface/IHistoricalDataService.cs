using CodeName399.Kite.API.Models.Indicator;

public interface IHistoricalDataService
{
    Task<List<Candle>> GetAsync(string token, string jwtToken, string interval = "FIVE_MINUTE", string exchange = "NSE");

}
