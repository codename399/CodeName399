using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Models.Dtos;
using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Repositories.Interface
{
    public interface IUserProjectMappingRepository
    {
        public Task<PagedResponse<UserProjectMappingDto>> GetAll(Request request);
        public Task<UserProjectMapping> GetAllWithProjects(string userId, string? roleName = "");
        public Task Update(UserProjectMappingDto userProjectMapping);
    }
}
