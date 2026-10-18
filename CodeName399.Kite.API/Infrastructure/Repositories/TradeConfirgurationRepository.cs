using CodeName399.Kite.API.Models.Configuration;
using CodeName399.Kite.API.Repositories.Interface;
using CodeName399.Model;
using DatabaseRespository.Repositories.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeName399.Kite.API.Repositories
{
    public class TradingConfigurationRepository
        : GlobalRepository<TradingConfiguration>,
            ITradingConfigurationRepository
    {
        private readonly IMongoCollection<TradingConfiguration> _collection;

        public TradingConfigurationRepository(
            IMongoClient mongoClient,
            IOptions<KiteDatabaseSetting> databaseSetting
        )
            : base(
                mongoClient,
                databaseSetting.Value,
                databaseSetting.Value.TradingConfigurationCollection ?? string.Empty
            )
        {
            _collection = _mongoDbRepository.GetCollection();
        }

        public async Task<TradingConfiguration> GetAsync()
        {
            return await _collection.Find(x => x.Id == "DEFAULT").FirstOrDefaultAsync()
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
