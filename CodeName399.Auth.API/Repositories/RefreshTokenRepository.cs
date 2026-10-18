using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Repositories.Interface;
using CodeName399.Model;
using DatabaseRepository.Constants;
using DatabaseRepository.Model;
using DatabaseRespository.Repositories.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeName399.Auth.API.Repositories
{
    public class RefreshTokenRepository : GlobalRepository<RefreshToken>, IRefreshTokenRepository
    {
        public RefreshTokenRepository(IOptions<AuthenticationDatabaseSetting> databaseSetting)
            : base(databaseSetting.Value, databaseSetting.Value.RefreshTokenCollection ?? string.Empty)
        {
        }

        public async Task Add(RefreshToken refreshToken)
        {
            if (refreshToken is null)
            {
                throw new InvalidOperationException(ErrorMessageConstant.NullParameter);
            }

            await DeleteByUserId(refreshToken.UserId);

            await _mongoDbRepository.Add([refreshToken]);
        }

        public async Task<string?> GetUserIdByRefreshToken(string refreshToken)
        {
            FilterDefinition<RefreshToken> filter = Builders<RefreshToken>.Filter.Eq(x => x.Token, refreshToken) &
                                                 Builders<RefreshToken>.Filter.Eq(x => x.IsDeleted, false) &
                                                 Builders<RefreshToken>.Filter.Gt(x => x.ExpiryDate, DateTime.UtcNow);

            PagedResponse<RefreshToken> pagedResponse = await _mongoDbRepository.Get(filter);

            if (pagedResponse.Count > 0)
            {
                return pagedResponse.Items.FirstOrDefault()?.UserId;
            }

            return null;
        }

        public async Task DeleteByToken(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                throw new InvalidOperationException(ErrorMessageConstant.NullParameter);
            }

            FilterDefinition<RefreshToken> filter = Builders<RefreshToken>.Filter.Eq(x => x.Token, refreshToken);

            await Delete(filter);
        }

        private async Task DeleteByUserId(string userId)
        {
            if (string.IsNullOrEmpty(userId))
            {
                throw new InvalidOperationException(ErrorMessageConstant.NullParameter);
            }

            FilterDefinition<RefreshToken> filter = Builders<RefreshToken>.Filter.Eq(x => x.UserId, userId);

            await Delete(filter);
        }

        private async Task Delete(FilterDefinition<RefreshToken> filter)
        {
            await _mongoDbRepository.Deactivate(filter, true);
        }
    }
}
