using CodeName399.Auth.API.Model;
using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Models
{
    public class UserProjectMapping : Audit
    {
        public required string UserId { get; set; }
        public User? User { get; set; }
        public HashSet<string>? ProjectIds { get; set; }
        public HashSet<Project>? Projects { get; set; }
    }
}
