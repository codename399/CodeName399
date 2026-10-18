using CodeName399.Kite.API.Models;

namespace CodeName399.Kite.API.Services.Interface
{
    public interface IAngelMarketService
    {
        Task<List<Gainer>> GetTopGainersAsync();
    }
}
