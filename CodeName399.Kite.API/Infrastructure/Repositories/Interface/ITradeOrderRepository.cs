using CodeName399.Kite.API.Models;

public interface ITradeOrderRepository
{
    Task SaveAsync(TradeOrder trade);
    Task<TradeOrder?> GetAsync(string symbol);
    Task<List<TradeOrder>> GetOpenTradesAsync();
    Task UpdateAsync(TradeOrder trade);
    Task DeleteAsync(string symbolOrTradeId);
    Task CloseTradeAsync(string symbol);
    Task<Dictionary<string, TradeOrder>> GetAllOpenAsync();
    Task UpdateManyAsync(IEnumerable<TradeOrder> trades);
    Task<List<TradeOrder>> GetPendingTradesAsync();
    Task UpdateStatusAsync(string symbol, TradeStatus status);
}
