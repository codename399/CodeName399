using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Models.Dtos
{
    public class UserProjectMappingDto : Audit
    {
        public required string UserId { get; set; }
        public HashSet<string>? ProjectIds { get; set; }
    }
}
