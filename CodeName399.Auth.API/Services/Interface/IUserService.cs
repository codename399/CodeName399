using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Models.Dtos;
using DatabaseRepository.Model;

namespace CodeName399.Auth.API.Service.Interface
{
    public interface IUserService
    {
        public Task Add(HashSet<User> users);
        public Task Update(User user);
        public Task UpdatePassword(ChangePasswordRequest changePasswordRequest);
        public Task Delete(HashSet<string> ids);
        public Task<PagedResponse<UserDto>> GetAll(Request request);
        public Task<HashSet<UserDto>> GetById(HashSet<string> ids);
        public Task<HashSet<User>> GetByUsername(string field, string value);
        public Task<HashSet<UserDto>> GetByProject(string projectName);
    }
}
