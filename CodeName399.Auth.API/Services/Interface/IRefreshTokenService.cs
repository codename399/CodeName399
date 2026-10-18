namespace CodeName399.Auth.API.Services.Interface
{
    public interface IRefreshTokenService
    {
        public Task Add(string userId, string token);
        public Task DeleteByToken(string refreshToken);
        public Task<string?> GetUserIdByRefreshToken(string refreshToken);
    }
}
