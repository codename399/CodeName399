using CodeName399.Auth.API.Constants;
using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Repositories.Interface;
using CodeName399.Model;
using DatabaseRepository.Constants;
using DatabaseRepository.Model;
using DatabaseRespository.Repositories.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeName399.Auth.API.Repositories
{
    public class ProjectRepository : GlobalRepository<Project>, IProjectRepository
    {
        public ProjectRepository(IOptions<AuthenticationDatabaseSetting> databaseSettings)
            : base(databaseSettings.Value, databaseSettings.Value.ProjectCollection ?? string.Empty, new CollectionOption()
            {
                IndexOptions = [
                    new IndexOption()
                        {
                            Name= IndexConstant.ProjectIndex,
                            Columns = [
                                BaseConstant.Name,
                                BaseConstant.Route
                        ]
                     }
                    ]
            })
        {

        }

        public async Task Add(HashSet<Project> projects)
        {
            if (projects is { Count: <= 0 })
            {
                throw new InvalidOperationException(ErrorMessageConstant.EmptyList);
            }

            await _mongoDbRepository.Add(projects);
        }

        public async Task Delete(HashSet<string> ids)
        {
            if (ids is { Count: <= 0 })
            {
                throw new InvalidOperationException(ErrorMessageConstant.EmptyList);
            }

            await _mongoDbRepository.Delete(ids);
        }

        public async Task<PagedResponse<Project>> GetAll(Request request)
        {
            return await _mongoDbRepository.GetAll(request);
        }

        public async Task<HashSet<Project>> GetById(HashSet<string> ids)
        {
            if (ids is { Count: <= 0 })
            {
                throw new InvalidOperationException(ErrorMessageConstant.EmptyList);
            }

            PagedResponse<Project> pagedResponse = await _mongoDbRepository.GetById(ids);

            return pagedResponse.Items;
        }

        public async Task Update(Project project)
        {
            if (project is null)
            {
                throw new ArgumentNullException(ErrorMessageConstant.NullParameter);
            }

            FilterDefinition<Project> filter = Builders<Project>.Filter.Eq(p => p.Id, project.Id);

            UpdateDefinition<Project> update = Builders<Project>.Update
                .Set(p => p.Name, project.Name)
                .Set(p => p.Description, project.Description)
                .Set(p => p.ImageUrl, project.ImageUrl)
                .Set(p => p.Route, project.Route)
                .Set(p => p.NavigationText, project.NavigationText)
                .Set(p => p.IsAdmin, project.IsAdmin)
                .Set(p => p.UpdationDate, DateTime.UtcNow)
                .Set(p => p.IsDeleted, project.IsDeleted);

            await _mongoDbRepository.Update(filter, update, true);
        }
    }
}
