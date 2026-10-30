using CodeName399.Kite.API.Models;
using CodeName399.Model;
using DatabaseRespository.Repositories.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeName399.Kite.API.Repository
{
    public class TradeOrderRepository : GlobalRepository<TradeOrder>, ITradeOrderFileRepository
    {
        private readonly IMongoCollection<TradeOrder> _collection;

        public TradeOrderRepository(
            IMongoClient mongoClient,
            IOptions<KiteDatabaseSetting> databaseSetting
        )
            : base(
                mongoClient,
                databaseSetting.Value,
                databaseSetting.Value.TradeOrderCollection ?? string.Empty
            )
        {
            _collection = _mongoDbRepository.GetCollection();
        }

        public async Task SaveAsync(TradeOrder trade)
        {
            trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();

            var existing = await _collection.Find(x => !x.Closed &&
                (!string.IsNullOrWhiteSpace(trade.SymbolToken)
                    ? x.SymbolToken == trade.SymbolToken && x.Exchange == trade.Exchange
                    : x.Symbol == trade.Symbol)).FirstOrDefaultAsync();

            if (existing != null)
                throw new InvalidOperationException(
                    $"Open trade already exists for {trade.Symbol}"
                );

            await _collection.InsertOneAsync(trade);
        }

        public async Task<List<TradeOrder>> GetOpenTradesAsync()
        {
            return await _collection.Find(x => !x.Closed).ToListAsync();
        }

        public async Task<TradeOrder?> GetBySymbolAsync(string symbol)
        {
            symbol = symbol.Trim().ToUpperInvariant();

            return await _collection
                .Find(x => x.Symbol == symbol && !x.Closed)
                .FirstOrDefaultAsync();
        }

        public Task DeleteAsync(string symbol)
        {
            return _collection.DeleteOneAsync(x => x.Symbol == symbol);
        }

        public Task CloseTradeAsync(string symbol)
        {
            return _collection.UpdateOneAsync(
                x => x.Symbol == symbol,
                Builders<TradeOrder>.Update.Set(x => x.Closed, true)
            );
        }

        public async Task<TradeOrder?> GetByPositionAsync(string exchange, string symbolToken)
        {
            if (string.IsNullOrWhiteSpace(symbolToken)) return null;
            return await _collection.Find(x => x.Exchange == exchange && x.SymbolToken == symbolToken && !x.Closed).FirstOrDefaultAsync();
        }

        public Task CloseByPositionAsync(string exchange, string symbolToken)
        {
            return _collection.UpdateOneAsync(x => x.Exchange == exchange && x.SymbolToken == symbolToken && !x.Closed,
                Builders<TradeOrder>.Update.Set(x => x.Closed, true).Set(x => x.Status, TradeStatus.Closed));
        }

        public async Task<TradeOrder?> GetAsync(string symbol)
        {
            symbol = symbol.Trim().ToUpperInvariant();

            return await _collection
                .Find(x => x.Symbol == symbol && !x.Closed)
                .FirstOrDefaultAsync();
        }

        public async Task UpdateAsync(TradeOrder trade)
        {
            trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();

            var filter = !string.IsNullOrWhiteSpace(trade.Id)
                ? Builders<TradeOrder>.Filter.Eq(x => x.Id, trade.Id)
                : Builders<TradeOrder>.Filter.And(
                    Builders<TradeOrder>.Filter.Eq(x => x.Symbol, trade.Symbol),
                    Builders<TradeOrder>.Filter.Eq(x => x.Closed, false)
                );

            await _collection.ReplaceOneAsync(filter, trade);
        }

        public async Task<Dictionary<string, TradeOrder>> GetAllOpenAsync()
        {
            var trades = await _collection
                .Find(x => !x.IsDeleted && x.Status == TradeStatus.Open && !x.Closed)
                .ToListAsync();

            return trades.ToDictionary(x => CodeName399.Kite.API.Helpers.PositionIdentity.Create(x), StringComparer.OrdinalIgnoreCase);
        }

        public async Task UpdateManyAsync(IEnumerable<TradeOrder> trades)
        {
            var models = trades
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .Select(x => new ReplaceOneModel<TradeOrder>(
                    Builders<TradeOrder>.Filter.Eq(t => t.Id, x.Id),
                    x
                ));

            await _collection.BulkWriteAsync(models);
        }

        public async Task<List<TradeOrder>> GetClosedTradesAsync(DateTime from, DateTime to)
        {
            var filter = Builders<TradeOrder>.Filter.And(
                Builders<TradeOrder>.Filter.Eq(x => x.Closed, true),
                Builders<TradeOrder>.Filter.Gte(x => x.ExitTime, from),
                Builders<TradeOrder>.Filter.Lt(x => x.ExitTime, to)
            );

            return await _collection.Find(filter).SortBy(x => x.ExitTime).ToListAsync();
        }

        public async Task<List<TradeOrder>> GetPendingTradesAsync()
        {
            return await _collection
                .Find(x => !x.IsDeleted && x.Status == TradeStatus.PendingBrokerConfirmation)
                .ToListAsync();
        }

        public async Task UpdateStatusAsync(string symbol, TradeStatus status)
        {
            var update = Builders<TradeOrder>.Update.Set(x => x.Status, status);

            await _collection.UpdateOneAsync(x => x.Symbol == symbol && !x.Closed, update);
        }

        public async Task<List<TradeOrder>> GetAllClosedTradesAsync()
        {
            return await _collection
                .Find(x => x.Closed)
                .SortBy(x => x.Symbol)
                .ThenBy(x => x.ExitTime)
                .ToListAsync();
        }
    }
}
