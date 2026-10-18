using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Repositories.Interface;
using CodeName399.Auth.API.Services.Interface;

namespace CodeName399.Auth.API.Services
{
    public class RefreshTokenService : IRefreshTokenService
    {
        private IRefreshTokenRepository _refreshTokenRepository;

        public RefreshTokenService(IRefreshTokenRepository refreshTokenRepository)
        {
            _refreshTokenRepository = refreshTokenRepository;
        }

        public async Task<string?> GetUserIdByRefreshToken(string refreshToken)
        {
            return await _refreshTokenRepository.GetUserIdByRefreshToken(refreshToken);
        }

        public async Task Add(string userId, string token)
        {
            RefreshToken refreshToken = new RefreshToken()
            {
                UserId = userId,
                Token = token,
                ExpiryDate = DateTime.UtcNow.AddDays(7)
            };

            await _refreshTokenRepository.Add(refreshToken);
        }

        public async Task DeleteByToken(string refreshToken)
        {
            await _refreshTokenRepository.DeleteByToken(refreshToken);
        }
    }
}
