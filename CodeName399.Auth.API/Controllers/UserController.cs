using AutoMapper;
using CodeName399.Auth.API.Constants;
using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Models.Dtos;
using CodeName399.Auth.API.Service.Interface;
using CodeName399.Auth.API.Services.Interface;
using DatabaseRepository.Common.Utilities;
using DatabaseRepository.Constants;
using DatabaseRepository.Model;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace CodeName399.Auth.API.Controllers
{

    public class UserController : Controller
    {
        private IUserService _userService;
        private IRoleService _roleService;
        private IUserProjectMappingService _userProjectMappingService;
        private IValidator<User> _userValidator;
        private IValidator<ChangePasswordRequest> _changePasswordRequestValidator;
        private IValidator<LoginUserRequest> _loginRequestValidator;
        private IValidator<string> _idValidator;
        private IMapper _mapper;
        private IRefreshTokenService _refreshTokenService;

        public UserController(IUserService userService, IRoleService roleService, IValidator<User> uservalidator, IValidator<ChangePasswordRequest> changePasswordRequestValidator, IValidator<string> idValidator, IMapper mapper, IValidator<LoginUserRequest> loginRequestValidator, IUserProjectMappingService userProjectMappingService, IRefreshTokenService refreshTokenService)
        {
            _userService = userService;
            _roleService = roleService;
            _userValidator = uservalidator;
            _changePasswordRequestValidator = changePasswordRequestValidator;
            _idValidator = idValidator;
            _mapper = mapper;
            _loginRequestValidator = loginRequestValidator;
            _userProjectMappingService = userProjectMappingService;
            _refreshTokenService = refreshTokenService;
        }

        [Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
        [HttpPost(ApiConstant.GetAllUsers)]
        public async Task<ActionResult<PagedResponse<UserDto>>> GetAll([FromBody] Request request)
        {
            PagedResponse<UserDto> users = await _userService.GetAll(request);
            return Ok(users);
        }

        [Authorize]
        [HttpGet(ApiConstant.GetUserById)]
        public async Task<ActionResult<HashSet<UserDto>>> GetById([FromQuery] string id)
        {
            await _idValidator.ValidateAndThrowAsync(id);

            HashSet<UserDto> users = await _userService.GetById([id]);
            return Ok(users);
        }

        [AllowAnonymous]
        [HttpPost(ApiConstant.AddUsers)]
        public async Task<IActionResult> Add([FromBody] User user)
        {
            await _userValidator.ValidateAndThrowAsync(user);

            await _userService.Add([user]);
            return Ok();
        }

        [Authorize]
        [HttpPut(ApiConstant.UpdateUser)]
        public async Task<IActionResult> Update([FromBody] User user)
        {
            await _userValidator.ValidateAndThrowAsync(user);

            await _userService.Update(user);
            return Ok();
        }

        [Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
        [HttpPost(ApiConstant.DeleteUser)]
        public async Task<IActionResult> Delete([FromBody] HashSet<string> ids)
        {
            foreach (string id in ids)
            {
                await _idValidator.ValidateAndThrowAsync(id);
            }

            await _userService.Delete(ids);
            return Ok();
        }

        [AllowAnonymous]
        [HttpPost(ApiConstant.ValidateUser)]
        public async Task<ActionResult<LoginResponse>> Validate([FromBody] LoginUserRequest loginRequest)
        {
            await _loginRequestValidator.ValidateAndThrowAsync(loginRequest);

            HashSet<User> users = await _userService.GetByUsername(BaseConstant.Username, loginRequest.Username);

            if (users is { Count: <= 0 })
            {
                throw new KeyNotFoundException("User not found.");
            }

            User user = users.FirstOrDefault();

            if (!PasswordHasher.VerifyPassword(loginRequest.Password, user.Password))
            {
                throw new UnauthorizedAccessException("Invalid password.");
            }

            HashSet<Claim> claims
                = await GenerateClaims(user);

            string jwtToken = Util.GenerateJwtToken(claims, BaseConstant.PasswordBasedAuthenticationConfig);
            string refreshToken = Util.GenerateRefreshToken();

            await _refreshTokenService.Add(user.Id, refreshToken);

            LoginResponse loginResponse = new LoginResponse()
            {
                Token = jwtToken,
                RefreshToken = refreshToken,
                UserId = user.Id
            };

            return Ok(loginResponse);
        }

        private async Task<HashSet<Claim>> GenerateClaims(User user)
        {
            HashSet<Role> roles = await _roleService.GetById([user.RoleId]);

            if (roles is { Count: <= 0 })
            {
                throw new KeyNotFoundException("Role not found for the user.");
            }

            Role role = roles.FirstOrDefault();

            UserProjectMapping userProjectMappingWithProjects = await _userProjectMappingService.GetAllWithProjects(user.Id);

            HashSet<Claim> claims = new HashSet<Claim>();
            claims.Add(new Claim(ClaimTypes.Role, role.Name));
            claims.Add(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
            claims.Add(new Claim(ClaimTypes.Name, user.Username));

            if (userProjectMappingWithProjects is not null && userProjectMappingWithProjects.Projects is { Count: > 0 })
            {
                foreach (Project project in userProjectMappingWithProjects.Projects)
                {
                    claims.Add(new Claim(project.Name.Replace(" ", ""), project.IsAdmin.ToString()));
                }
            }

            return claims;
        }

        [Authorize]
        [HttpPost(ApiConstant.ChangePassword)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest changePasswordRequest)
        {
            await _changePasswordRequestValidator.ValidateAndThrowAsync(changePasswordRequest);

            HashSet<User> users = await _userService.GetByUsername(BaseConstant.Username, changePasswordRequest.Username);

            if (users is { Count: <= 0 })
            {
                throw new KeyNotFoundException("User not found.");
            }

            User user = users.FirstOrDefault();

            if (!PasswordHasher.VerifyPassword(changePasswordRequest.Password, user.Password))
            {
                throw new UnauthorizedAccessException("Invalid password.");
            }

            changePasswordRequest.Password = PasswordHasher.HashPassword(changePasswordRequest.NewPassword);

            await _userService.UpdatePassword(changePasswordRequest);

            return Ok();
        }

        [Authorize]
        [HttpGet(ApiConstant.GetByProject)]
        public async Task<ActionResult<HashSet<UserDto>>> GetByProject([FromQuery] string projectName)
        {
            if (string.IsNullOrEmpty(projectName))
            {
                throw new ArgumentException();
            }

            HashSet<UserDto> users = await _userService.GetByProject(projectName);
            return Ok(users);
        }

        [AllowAnonymous]
        [HttpPost(ApiConstant.Refresh)]
        public async Task<ActionResult<LoginResponse>> Refresh([FromBody]
        RefreshTokenRequest refreshTokenRequest)
        {
            string? userId =
                await _refreshTokenService.GetUserIdByRefreshToken(
                    refreshTokenRequest.RefreshToken);

            if (userId == null)
            {
                throw new KeyNotFoundException("User not found.");
            }

            await _refreshTokenService.DeleteByToken(refreshTokenRequest.RefreshToken);

            HashSet<UserDto> users = await _userService.GetById([userId]);

            if (users is { Count: <= 0 })
            {
                throw new KeyNotFoundException("User not found.");
            }

            User user = _mapper.Map<User>(users.FirstOrDefault());

            HashSet<Claim> claims
                = await GenerateClaims(user);

            string newToken =
                Util.GenerateJwtToken(claims, BaseConstant.PasswordBasedAuthenticationConfig);

            string newRefreshToken =
                Util.GenerateRefreshToken();

            await _refreshTokenService.Add(
                userId,
                newRefreshToken);

            LoginResponse loginResponse = new LoginResponse()
            {
                Token = newToken,
                RefreshToken = newRefreshToken,
                UserId = userId
            };

            return Ok(loginResponse);
        }
    }
}
