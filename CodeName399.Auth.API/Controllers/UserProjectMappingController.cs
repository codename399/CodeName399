using CodeName399.Auth.API.Constants;
using CodeName399.Auth.API.Models.Dtos;
using CodeName399.Auth.API.Services.Interface;
using DatabaseRepository.Model;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeName399.Auth.API.Controllers
{

    public class UserProjectMappingController : Controller
    {
        private IUserProjectMappingService _userProjectMappingService;
        private IValidator<UserProjectMappingDto> _userProjectMappingValidator;

        public UserProjectMappingController(IUserProjectMappingService userProjectMappingService, IValidator<UserProjectMappingDto> userProjectMappingValidator)
        {
            _userProjectMappingService = userProjectMappingService;
            _userProjectMappingValidator = userProjectMappingValidator;
        }

        [Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
        [HttpPost(ApiConstant.GetAllUserProjectMappings)]
        public async Task<ActionResult<PagedResponse<UserDto>>> GetAll([FromBody] Request request)
        {
            PagedResponse<UserProjectMappingDto> pagedResponse = await _userProjectMappingService.GetAll(request);
            return Ok(pagedResponse);
        }

        [Authorize]
        [HttpPut(ApiConstant.UpdateUserProjectMappings)]
        public async Task<IActionResult> Update([FromBody] UserProjectMappingDto userProjectMapping)
        {
            await _userProjectMappingValidator.ValidateAndThrowAsync(userProjectMapping);

            await _userProjectMappingService.Update(userProjectMapping);
            return Ok();
        }
    }
}
