using CodeName399.Kite.API.Models;

public interface ITradeOrderFileRepository
{
    Task SaveAsync(TradeOrder trade);
    Task<TradeOrder?> GetAsync(string symbol);
    Task<List<TradeOrder>> GetOpenTradesAsync();
    Task UpdateAsync(TradeOrder trade);
    Task DeleteAsync(string symbolOrTradeId);
}
