namespace CodeName399.Auth.API.Models
{
    public class LoginResponse
    {
        public required string Token { get; set; }
        public required string RefreshToken { get; set; }
        public required string UserId { get; set; }
    }
}
