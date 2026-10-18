namespace CodeName399.Auth.API.Models
{
    public class LoginUserRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
    }
}
