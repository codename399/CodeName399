using CodeName399.Auth.API.Models;
using FluentValidation;

namespace CodeName399.Auth.API.Validators
{
    public class LoginRequestValidator : AbstractValidator<LoginUserRequest>
    {
        public LoginRequestValidator()
        {
            RuleFor(x => x.Username)
                .NotNull()
                .NotEmpty();

            RuleFor(x => x.Password)
                .NotNull()
                .NotEmpty();
        }
    }
}
