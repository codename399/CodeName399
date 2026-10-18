using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Repositories.Interface;
using CodeName399.Auth.API.Services.Interface;
using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Services
{
    public class ProjectService : IProjectService
    {
        private IProjectRepository _projectRepository;

        public ProjectService(IProjectRepository projectRepository)
        {
            _projectRepository = projectRepository;
        }

        public async Task Add(HashSet<Project> projects)
        {
            await _projectRepository.Add(projects);
        }

        public async Task Delete(HashSet<string> ids)
        {
            await _projectRepository.Delete(ids);
        }

        public async Task<PagedResponse<Project>> GetAll(Request request)
        {
            return await _projectRepository.GetAll(request);
        }

        public async Task<HashSet<Project>> GetById(HashSet<string> ids)
        {
            return await (_projectRepository.GetById(ids));
        }

        public async Task Update(Project project)
        {
            await (_projectRepository.Update(project));
        }
    }
}
