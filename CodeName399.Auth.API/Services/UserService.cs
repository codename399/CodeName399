using AutoMapper;
using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Models.Dtos;
using CodeName399.Auth.API.Repository.Interface;
using CodeName399.Auth.API.Service.Interface;
using DatabaseRepository.Common.Utilities;
using DatabaseRepository.Constants;
using DatabaseRepository.Model;
using DatabaseRepository.Model.Enum;

namespace CodeName399.Auth.API.Service
{
    public class UserService : IUserService
    {
        private IUserRepository _userRepository;
        private IRoleService _roleService;
        private IMapper _mapper;
        private ILogger<UserService> _logger;

        public UserService(IUserRepository userRepository, IRoleService roleService, IMapper mapper, ILogger<UserService> logger)
        {
            _userRepository = userRepository;
            _roleService = roleService;
            _mapper = mapper;
            _logger = logger;
        }

        public async Task Add(HashSet<User> users)
        {
            if (users is { Count: <= 0 })
            {
                _logger.LogError("Users not found as {Time}", DateTime.UtcNow);
            }

            Request request = new Request()
            {
                Filters = new HashSet<FilterRequest>() {
                    new FilterRequest {
                        Key = BaseConstant.Name,
                        Value = BaseConstant.Standard,
                        Operator = OperatorType.Equal.ToString()
                    }
                },
                FetchAll = true
            };
            var roles = await _roleService.GetAll(request);

            Role role = roles.Items.FirstOrDefault();

            users = users.Select(u =>
            {
                u.Password = PasswordHasher.HashPassword(u.Password);
                u.RoleId = u.RoleId ?? role?.Id;
                return u;
            }).ToHashSet();

            await _userRepository.Add(users);
        }

        public async Task Delete(HashSet<string> ids)
        {
            await _userRepository.Delete(ids);
        }

        public async Task<PagedResponse<UserDto>> GetAll(Request request)
        {
            return await _userRepository.GetAll(request);
        }

        public async Task<HashSet<UserDto>> GetById(HashSet<string> ids)
        {
            return await _userRepository.GetById(ids);
        }

        public async Task<HashSet<UserDto>> GetByProject(string projectName)
        {
            return await _userRepository.GetByProject(projectName);
        }

        public async Task<HashSet<User>> GetByUsername(string field, string value)
        {
            return await _userRepository.GetByUsername(field, value);
        }

        public async Task Update(User user)
        {
            Request request = new Request()
            {
                Filters = new HashSet<FilterRequest>() {
                    new FilterRequest {
                        Key = BaseConstant.Name,
                        Value = BaseConstant.Standard,
                        Operator = OperatorType.Equal.ToString()
                    }
                },
                FetchAll = true
            };
            var roles = await _roleService.GetAll(request);

            Role role = roles.Items.FirstOrDefault();

            user.RoleId = user.RoleId ?? role?.Id;

            await _userRepository.Update(user);
        }

        public async Task UpdatePassword(ChangePasswordRequest changePasswordRequest)
        {
            await _userRepository.UpdatePassword(changePasswordRequest);
        }
    }
}
