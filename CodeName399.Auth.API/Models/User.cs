using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Model
{
    public class User : Audit
    {
        public required string Name { get; set; }
        public required string Username { get; set; }
        public required string Password { get; set; }
        public required string EmailId { get; set; }
        public required string ContactNumber { get; set; }
        public string? RoleId { get; set; }
        public string? ProfilePicture { get; set; }
    }
}
