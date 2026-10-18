using CodeName399.Auth.API.Models;
using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Repositories.Interface
{
    public interface IProjectRepository
    {
        public Task Add(HashSet<Project> projects);
        public Task Update(Project project);
        public Task Delete(HashSet<string> ids);
        public Task<PagedResponse<Project>> GetAll(Request request);
        public Task<HashSet<Project>> GetById(HashSet<string> ids);
    }
}
