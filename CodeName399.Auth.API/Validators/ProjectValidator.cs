using CodeName399.Auth.API.Models;
using FluentValidation;

namespace CodeName399.Auth.API.Validators
{
    public class ProjectValidator : AbstractValidator<Project>
    {
        public ProjectValidator()
        {
            RuleFor(x => x.Name)
                .NotNull()
                .NotEmpty();
        }
    }
}
