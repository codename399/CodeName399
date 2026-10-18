using CodeName399.Auth.API.Models;

namespace CodeName399.Auth.API.Repositories.Interface
{
    public interface IRefreshTokenRepository
    {
        public Task Add(RefreshToken refreshToken);
        public Task DeleteByToken(string refreshToken);
        public Task<string?> GetUserIdByRefreshToken(string refreshToken);
    }
}
