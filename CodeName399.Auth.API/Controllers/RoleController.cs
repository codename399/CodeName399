using CodeName399.Auth.API.Constants;
using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Service.Interface;
using DatabaseRepository.Model;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeName399.Auth.API.Controllers
{
    [Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
    public class RoleController : Controller
    {
        private IRoleService _roleService;
        private IValidator<Role> _roleValidator;
        private IValidator<string> _idValidator;

        public RoleController(IRoleService roleService, IValidator<Role> roleValidator, IValidator<string> idValidator)
        {
            _roleService = roleService;
            _roleValidator = roleValidator;
            _idValidator = idValidator;
        }

        [HttpPost(ApiConstant.GetAllRoles)]
        public async Task<ActionResult<PagedResponse<Role>>> GetAll([FromBody] Request request)
        {
            PagedResponse<Role> roles = await _roleService.GetAll(request);
            return Ok(roles);
        }

        [HttpGet(ApiConstant.GetRoleById)]
        public async Task<ActionResult<HashSet<Role>>> GetById([FromQuery] string id)
        {
            await _idValidator.ValidateAndThrowAsync(id);

            HashSet<Role> roles = await _roleService.GetById([id]);
            return Ok(roles);
        }

        [HttpPost(ApiConstant.AddRoles)]
        public async Task<IActionResult> Add([FromBody] Role role)
        {
            await _roleValidator.ValidateAndThrowAsync(role);

            await _roleService.Add([role]);
            return Ok();
        }

        [HttpPut(ApiConstant.UpdateRole)]
        public async Task<IActionResult> Update([FromBody] Role role)
        {
            await _roleValidator.ValidateAndThrowAsync(role);

            await _roleService.Update(role);
            return Ok();
        }

        [HttpPost(ApiConstant.DeleteRole)]
        public async Task<IActionResult> Delete([FromBody] HashSet<string> ids)
        {
            foreach (string id in ids)
            {
                await _idValidator.ValidateAndThrowAsync(id);
            }

            await _roleService.Delete(ids);
            return Ok();
        }
    }
}
