namespace CodeName399.Auth.API.Models
{
    public class ChangePasswordRequest
    {
        public required string Username { get; set; }
        public required string Password { get; set; }
        public required string NewPassword { get; set; }
    }
}
