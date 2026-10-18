using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Model
{
    public class Role : Audit
    {
        public required string Name { get; set; }
    }
}
