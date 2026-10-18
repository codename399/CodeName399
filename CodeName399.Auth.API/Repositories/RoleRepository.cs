using CodeName399.Auth.API.Constants;
using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Repository.Interface;
using CodeName399.Model;
using DatabaseRepository.Constants;
using DatabaseRepository.Model;
using DatabaseRespository.Repositories.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeName399.Auth.API.Repository
{
    public class RoleRepository : GlobalRepository<Role>, IRoleRepository
    {
        public RoleRepository(IOptions<AuthenticationDatabaseSetting> databaseSetting)
            : base(databaseSetting.Value, databaseSetting.Value.RoleCollection ?? string.Empty
                  , new CollectionOption()
                  {
                      IndexOptions = [
                    new IndexOption()
                        {
                            Name= IndexConstant.RoleIndex,
                            Columns = [
                                BaseConstant.Name
                        ]
                     }
                    ]
                  })
        {

        }

        public async Task Add(HashSet<Role> roles)
        {
            if (roles is { Count: <= 0 })
            {
                throw new InvalidOperationException(ErrorMessageConstant.EmptyList);
            }

            await _mongoDbRepository.Add(roles);
        }

        public async Task Delete(HashSet<string> ids)
        {
            if (ids is { Count: <= 0 })
            {
                throw new InvalidOperationException(ErrorMessageConstant.EmptyList);
            }

            await _mongoDbRepository.Delete(ids);
        }

        public async Task<PagedResponse<Role>> GetAll(Request request)
        {
            return await _mongoDbRepository.GetAll(request);
        }

        public async Task<HashSet<Role>> GetById(HashSet<string> ids)
        {
            PagedResponse<Role> pagedResponse = await _mongoDbRepository.GetById(ids);
            return pagedResponse.Items;
        }

        public async Task Update(Role role)
        {
            if (role is null)
            {
                throw new InvalidOperationException(ErrorMessageConstant.NullParameter);
            }

            FilterDefinition<Role> filter = Builders<Role>.Filter.Eq(r => r.Id, role.Id);

            UpdateDefinition<Role> update = Builders<Role>.Update
                .Set(r => r.Name, role.Name)
                .Set(r => r.UpdationDate, DateTime.UtcNow)
                .Set(r => r.IsDeleted, role.IsDeleted);

            await _mongoDbRepository.Update(filter, update, true);
        }
    }
}
