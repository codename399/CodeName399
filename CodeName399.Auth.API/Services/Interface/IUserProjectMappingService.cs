using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Models.Dtos;
using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Services.Interface
{
    public interface IUserProjectMappingService
    {
        public Task<PagedResponse<UserProjectMappingDto>> GetAll(Request request);
        public Task<UserProjectMapping> GetAllWithProjects(string userId);
        public Task Update(UserProjectMappingDto userProjectMapping);
    }
}
