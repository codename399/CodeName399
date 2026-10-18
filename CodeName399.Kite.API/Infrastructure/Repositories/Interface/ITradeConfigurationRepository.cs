using CodeName399.Kite.API.Models.Configuration;

namespace CodeName399.Kite.API.Repositories.Interface
{
    public interface ITradingConfigurationRepository
    {
        Task<TradingConfiguration> GetAsync();

        Task UpdateAsync(TradingConfiguration configuration);
    }
}
