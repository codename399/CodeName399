using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Models.Dtos;
using CodeName399.Auth.API.Repositories.Interface;
using CodeName399.Auth.API.Repository.Interface;
using CodeName399.Auth.API.Services.Interface;
using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Services
{
    public class UserProjectMappingService : IUserProjectMappingService
    {
        private readonly IUserProjectMappingRepository _userProjectMappingRepository;
        private readonly IRoleRepository _roleRepository;
        private IUserRepository _userRepository;

        public UserProjectMappingService(IUserProjectMappingRepository userProjectMappingRepository, IRoleRepository roleRepository, IUserRepository userRepository)
        {
            _userProjectMappingRepository = userProjectMappingRepository;
            _roleRepository = roleRepository;
            _userRepository = userRepository;
        }

        public async Task<PagedResponse<UserProjectMappingDto>> GetAll(Request request)
        {
            return await _userProjectMappingRepository.GetAll(request);
        }

        public async Task<UserProjectMapping> GetAllWithProjects(string userId)
        {
            string roleName = string.Empty;

            HashSet<UserDto> users = await _userRepository.GetById([userId]);

            if (users is { Count: > 0 })
            {
                HashSet<Role> roles = await _roleRepository.GetById([users.First().RoleId]);

                if (roles is { Count: > 0 })
                {
                    roleName = roles.First().Name;
                }
            }

            return await _userProjectMappingRepository.GetAllWithProjects(userId, roleName);
        }

        public async Task Update(UserProjectMappingDto userProjectMapping)
        {
            await _userProjectMappingRepository.Update(userProjectMapping);
        }
    }
}
