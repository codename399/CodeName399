using CodeName399.Kite.API.Models.Configuration;
using CodeName399.Kite.API.Repositories.Interface;
using CodeName399.Model;
using DatabaseRespository.Repositories.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using System.Text.Json;

namespace CodeName399.Kite.API.Repositories
{
    public class TradingConfigurationRepository
        : GlobalRepository<TradingConfiguration>,
            ITradingConfigurationRepository
    {
        private readonly IMongoCollection<TradingConfiguration> _collection;
        private readonly TradingConfiguration _defaults;

        public TradingConfigurationRepository(
            IMongoClient mongoClient,
            IOptions<KiteDatabaseSetting> databaseSetting,
            IOptions<TradingConfiguration> tradingConfiguration
        )
            : base(
                mongoClient,
                databaseSetting.Value,
                databaseSetting.Value.TradingConfigurationCollection ?? string.Empty
            )
        {
            _collection = _mongoDbRepository.GetCollection();
            _defaults = tradingConfiguration.Value;
        }

        public async Task<TradingConfiguration> GetAsync()
        {
            var persisted = await _collection.Find(x => x.Id == "DEFAULT").FirstOrDefaultAsync();
            if (persisted != null) return persisted;

            // First-run configuration comes from appsettings.json; clone it so
            // callers can update the live snapshot without mutating IOptions.
            return JsonSerializer.Deserialize<TradingConfiguration>(JsonSerializer.Serialize(_defaults))
                ?? new TradingConfiguration();
        }

        public async Task UpdateAsync(TradingConfiguration configuration)
        {
            await _collection.ReplaceOneAsync(
                x => x.Id == configuration.Id,
                configuration,
                new ReplaceOptions { IsUpsert = true }
            );
        }
    }
}
