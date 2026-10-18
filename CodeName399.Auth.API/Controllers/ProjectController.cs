using CodeName399.Auth.API.Constants;
using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Services.Interface;
using DatabaseRepository.Model;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeName399.Auth.API.Controllers
{
    public class ProjectController : Controller
    {
        private IProjectService _projectService;
        private IValidator<Project> _projectValidator;
        private IValidator<string> _idValidator;
        private IUserProjectMappingService _userProjectMappingService;

        public ProjectController(IProjectService projectService, IValidator<Project> projectValidator, IValidator<string> idValidator, IUserProjectMappingService userProjectMappingService)
        {
            _projectService = projectService;
            _projectValidator = projectValidator;
            _idValidator = idValidator;
            _userProjectMappingService = userProjectMappingService;
        }

        [Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
        [HttpPost(ApiConstant.GetAllProjects)]
        public async Task<ActionResult<PagedResponse<Project>>> GetAll([FromBody] Request request)
        {
            PagedResponse<Project> projects = await _projectService.GetAll(request);
            return Ok(projects);
        }

        [Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
        [HttpGet(ApiConstant.GetProjectById)]
        public async Task<ActionResult<HashSet<Project>>> GetById([FromQuery] string id)
        {
            _idValidator.ValidateAndThrow(id);

            HashSet<Project> projects = await _projectService.GetById([id]);
            return Ok(projects);
        }

        [Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
        [HttpPost(ApiConstant.AddProjects)]
        public async Task<IActionResult> Add([FromBody] Project project)
        {
            await _projectValidator.ValidateAndThrowAsync(project);

            await _projectService.Add([project]);
            return Ok();
        }

        [Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
        [HttpPut(ApiConstant.UpdateProject)]
        public async Task<IActionResult> Update([FromBody] Project project)
        {
            await _projectValidator.ValidateAndThrowAsync(project);

            await _projectService.Update(project);
            return Ok();
        }

        [Authorize(Policy = PolicyConstant.AuthenticationAdmin)]
        [HttpPost(ApiConstant.DeleteProject)]
        public async Task<IActionResult> Delete([FromBody] HashSet<string> ids)
        {
            foreach (string id in ids)
            {
                await _idValidator.ValidateAndThrowAsync(id);
            }

            await _projectService.Delete(ids);
            return Ok();
        }

        [Authorize]
        [HttpGet(ApiConstant.GetAllMappedProjects)]
        public async Task<ActionResult<HashSet<Project>>> GetAllMappedProjects([FromQuery] string userId)
        {
            await _idValidator.ValidateAndThrowAsync(userId);

            UserProjectMapping userProjectMappingWithProjects = await _userProjectMappingService.GetAllWithProjects(userId);

            HashSet<Project> projects = new HashSet<Project>();

            if (userProjectMappingWithProjects is not null && userProjectMappingWithProjects.Projects is { Count: > 0 })
            {
                projects = userProjectMappingWithProjects.Projects;
            }

            return Ok(projects);
        }
    }
}
