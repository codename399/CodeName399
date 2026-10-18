using CodeName399.Auth.API.Constants;
using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Models.Dtos;
using CodeName399.Auth.API.Repositories.Interface;
using CodeName399.Model;
using DatabaseRepository.Constants;
using DatabaseRepository.Model;
using DatabaseRespository.Repositories.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeName399.Auth.API.Repositories
{
    public class UserProjectMappingRepository : GlobalRepository<UserProjectMappingDto>, IUserProjectMappingRepository
    {
        private IMongoDatabase _database;
        private IMongoCollection<Project> _projectCollection;
        private IMongoCollection<UserProjectMappingDto> _userProjectMappingCollection;

        public UserProjectMappingRepository(IOptions<AuthenticationDatabaseSetting> databaseSettings)
        : base(databaseSettings.Value, databaseSettings.Value.UserProjectMappingCollection ?? string.Empty,
              new CollectionOption()
              {
                  IndexOptions = [
                      new IndexOption(){
                          Name = IndexConstant.UserProjectMappingIndex,
                          Columns = [BaseConstant.UserId]
                      }
                      ]
              })
        {
            _database = _mongoDbRepository.GetDatabase();
            _projectCollection = _database.GetCollection<Project>(databaseSettings.Value.ProjectCollection);
            _userProjectMappingCollection = _mongoDbRepository.GetCollection();
        }

        public async Task<PagedResponse<UserProjectMappingDto>> GetAll(Request request)
        {
            return await _mongoDbRepository.GetAll(request);
        }

        public async Task<UserProjectMapping> GetAllWithProjects(string userId, string? roleName = "")
        {
            UserProjectMapping userProjectMappingWithProject = new UserProjectMapping() { UserId = userId };

            if (roleName == BaseConstant.Admin)
            {
                List<Project> projects = await _projectCollection.Aggregate().ToListAsync();

                userProjectMappingWithProject.Projects = new HashSet<Project>();
                userProjectMappingWithProject.Projects.UnionWith(projects);
            }
            else
            {
                List<UserProjectMapping> userProjectMappingWithProjects = await _userProjectMappingCollection.Aggregate()
               .Match(x => x.UserId == userId)
               .Match(x => x.ProjectIds != null && x.ProjectIds.Any())
                .Lookup<UserProjectMappingDto, Project, UserProjectMapping>(
                   _projectCollection,
                   m => m.ProjectIds,
                   p => p.Id,
                   op => op.Projects
                )
                .Project(p => new UserProjectMapping
                {
                    UserId = p.UserId,
                    ProjectIds = p.ProjectIds,
                    Projects = p.Projects
                })
                .ToListAsync();

                if (userProjectMappingWithProjects is { Count: > 0 })
                {
                    userProjectMappingWithProject = userProjectMappingWithProjects.Select(x =>
                    {
                        if (x.Projects is { Count: > 0 })
                        {
                            x.Projects = x.Projects.Where(x => x.IsAdmin == false).ToList().ToHashSet();
                        }

                        return x;
                    }).FirstOrDefault();
                }
            }

            return userProjectMappingWithProject;
        }

        public async Task Update(UserProjectMappingDto userProjectMapping)
        {
            if (userProjectMapping is null)
            {
                throw new InvalidOperationException(ErrorMessageConstant.NullParameter);
            }

            FilterDefinition<UserProjectMappingDto> filter = Builders<UserProjectMappingDto>.Filter.Eq(x => x.Id, userProjectMapping.Id);

            UpdateDefinition<UserProjectMappingDto> update = Builders<UserProjectMappingDto>.Update
                .Set(x => x.UserId, userProjectMapping.UserId)
                .Set(x => x.ProjectIds, userProjectMapping.ProjectIds)
                .Set(x => x.UpdationDate, DateTime.UtcNow)
                .Set(x => x.IsDeleted, userProjectMapping.IsDeleted);

            await _mongoDbRepository.Update(filter, update, true);
        }
    }
}
