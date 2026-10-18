using AutoMapper;
using CodeName399.Auth.API.Constants;
using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Models.Dtos;
using CodeName399.Auth.API.Repository.Interface;
using CodeName399.Model;
using DatabaseRepository.Constants;
using DatabaseRepository.Model;
using DatabaseRepository.Model.Enum;
using DatabaseRespository.Repositories.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeName399.Auth.API.Repository
{
    public class UserRepository : GlobalRepository<User>, IUserRepository
    {
        private IMapper _mapper;
        private IMongoDatabase _database;
        private IMongoCollection<User> _userCollection;
        private IMongoCollection<UserProjectMappingDto> _userProjectMappingCollection;
        private IMongoCollection<Project> _projectCollection;

        public UserRepository(IOptions<AuthenticationDatabaseSetting> databaseSetting
            , IMapper mapper)
            : base(databaseSetting.Value, databaseSetting.Value.UserCollection ?? string.Empty,
                  new CollectionOption()
                  {
                      IndexOptions = [
                    new IndexOption()
                        {
                            Name= IndexConstant.UserIndex,
                            Columns = [
                                BaseConstant.Username,
                                BaseConstant.EmailId,
                                BaseConstant.ContactNumber
                        ]
                     }
                    ]
                  })
        {
            _mapper = mapper;
            _database = _mongoDbRepository.GetDatabase();
            _userCollection = _database.GetCollection<User>(databaseSetting.Value.UserCollection);
            _userProjectMappingCollection = _database.GetCollection<UserProjectMappingDto>(databaseSetting.Value.UserProjectMappingCollection);
            _projectCollection = _database.GetCollection<Project>(databaseSetting.Value.ProjectCollection);
        }

        public async Task Add(HashSet<User> users)
        {
            if (users is { Count: <= 0 })
            {
                throw new InvalidOperationException(ErrorMessageConstant.EmptyList);
            }

            await _mongoDbRepository.Add(users);
        }

        public async Task Delete(HashSet<string> ids)
        {
            if (ids is { Count: <= 0 })
            {
                throw new InvalidOperationException(ErrorMessageConstant.EmptyList);
            }

            await _mongoDbRepository.Delete(ids);
        }

        public async Task<PagedResponse<UserDto>> GetAll(Request request)
        {
            var users = await _mongoDbRepository.GetAll(request);

            PagedResponse<UserDto> pagedResponse = new PagedResponse<UserDto>
            {
                Items = _mapper.Map<HashSet<UserDto>>(users.Items),
                Count = users.Count
            };

            return pagedResponse;
        }

        public async Task<HashSet<User>> GetByUsername(string field, string value)
        {
            if (string.IsNullOrEmpty(field) || string.IsNullOrEmpty(value))
            {
                throw new InvalidOperationException(ErrorMessageConstant.NullParameter);
            }

            Request request = new Request()
            {
                Filters = new HashSet<FilterRequest>() {
                    new FilterRequest {
                        Key = field,
                        Value = value,
                        Operator = OperatorType.Equal.ToString()
                    }
                },
                FetchAll = true
            };

            PagedResponse<User> pagedResponse = await _mongoDbRepository.GetAll(request);

            return pagedResponse.Items;
        }

        public async Task<HashSet<UserDto>> GetById(HashSet<string> ids)
        {
            var users = await _mongoDbRepository.GetById(ids);

            return _mapper.Map<HashSet<UserDto>>(users.Items);
        }

        public async Task Update(User user)
        {
            if (user is null)
            {
                throw new InvalidOperationException(ErrorMessageConstant.NullParameter);
            }

            FilterDefinition<User> filter = Builders<User>.Filter.Eq(u => u.Id, user.Id);

            UpdateDefinition<User> update = Builders<User>.Update
                .Set(u => u.RoleId, user.RoleId)
                .Set(u => u.Name, user.Name)
                .Set(u => u.EmailId, user.EmailId)
                .Set(u => u.ContactNumber, user.ContactNumber)
                .Set(u => u.ProfilePicture, user.ProfilePicture)
                .Set(u => u.UpdationDate, DateTime.UtcNow)
                .Set(u => u.IsDeleted, user.IsDeleted);

            await _mongoDbRepository.Update(filter, update, false);
        }

        public async Task UpdatePassword(ChangePasswordRequest changePasswordRequest)
        {
            if (changePasswordRequest is null)
            {
                throw new InvalidOperationException(ErrorMessageConstant.NullParameter);
            }

            FilterDefinition<User> filter = Builders<User>.Filter.Eq(u => u.Username, changePasswordRequest.Username);

            UpdateDefinition<User> update = Builders<User>.Update
                .Set(u => u.Password, changePasswordRequest.Password);

            await _mongoDbRepository.Update(filter, update, false);
        }

        public async Task<HashSet<UserDto>> GetByProject(string projectName)
        {
            List<Project> projects = await _projectCollection.Aggregate()
                .Match(x => x.Name == projectName).ToListAsync();
            HashSet<UserDto> userDtos = new HashSet<UserDto>();

            if (projects is { Count: > 0 })
            {
                string projectId = projects.First().Id ?? string.Empty;

                if (!string.IsNullOrEmpty(projectId))
                {
                    List<User> users = await _userProjectMappingCollection.Aggregate()
                    .Match(x => x.ProjectIds != null && x.ProjectIds.Contains(projectId))
                    .Lookup<UserProjectMappingDto, User, UserProjectMapping>(
                        _userCollection,
                        m => m.UserId,
                        u => u.Id,
                        op => op.User
                        )
                    .Unwind<UserProjectMapping, UserProjectMapping>(op => op.User)
                    .Project(p => p.User)
                    .ToListAsync();

                    if (users is { Count: > 0 })
                    {
                        userDtos = _mapper.Map<HashSet<UserDto>>(users);
                    }
                }
            }

            return userDtos;
        }
    }
}
