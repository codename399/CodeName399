using CodeName399.Auth.API.Model;
using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Service.Interface
{
    public interface IRoleService
    {
        public Task Add(HashSet<Role> roles);
        public Task Update(Role role);
        public Task Delete(HashSet<string> ids);
        public Task<PagedResponse<Role>> GetAll(Request request);
        public Task<HashSet<Role>> GetById(HashSet<string> ids);
    }
}
