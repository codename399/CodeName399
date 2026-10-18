using CodeName399.Auth.API.Model;
using FluentValidation;

namespace CodeName399.Auth.API.Validators
{
    public class RoleValidator : AbstractValidator<Role>
    {
        public RoleValidator()
        {
            RuleFor(x => x.Name)
                .NotNull()
                .NotEmpty();
        }
    }
}
