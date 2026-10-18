using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Models.Dtos
{
    public class UserDto : Audit
    {
        public required string Name { get; set; }
        public required string Username { get; set; }
        public required string EmailId { get; set; }
        public required string ContactNumber { get; set; }
        public required string RoleId { get; set; }
        public string? ProfilePicture { get; set; }
    }
}
