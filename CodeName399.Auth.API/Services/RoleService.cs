using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Repository.Interface;
using CodeName399.Auth.API.Service.Interface;
using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Service
{
    public class RoleService : IRoleService
    {
        private IRoleRepository _roleRepository;

        public RoleService(IRoleRepository roleRepository)
        {
            _roleRepository = roleRepository;
        }

        public async Task Add(HashSet<Role> roles)
        {
            await _roleRepository.Add(roles);
        }

        public async Task Delete(HashSet<string> ids)
        {
            await _roleRepository.Delete(ids);
        }

        public async Task<PagedResponse<Role>> GetAll(Request request)
        {
            return await _roleRepository.GetAll(request);
        }

        public async Task<HashSet<Role>> GetById(HashSet<string> ids)
        {
            return await _roleRepository.GetById(ids);
        }

        public async Task Update(Role role)
        {
            await _roleRepository.Update(role);
        }
    }
}
