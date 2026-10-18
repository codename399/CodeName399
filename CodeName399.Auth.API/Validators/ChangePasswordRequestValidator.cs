using CodeName399.Auth.API.Models;
using FluentValidation;

namespace CodeName399.Auth.API.Validators
{
    public class ChangePasswordRequestValidator : AbstractValidator<ChangePasswordRequest>
    {
        public ChangePasswordRequestValidator()
        {
            RuleFor(x => x.Username)
                .NotNull()
                .NotEmpty();

            RuleFor(x => x.Password)
                .NotNull()
                .NotEmpty();

            RuleFor(x => x.NewPassword)
                .NotNull()
                .NotEmpty()
                .NotEqual(x => x.Password).WithMessage("New password cannot be same as old password")
                .MinimumLength(8).WithMessage("New password must be at least 8 characters long")
                .Matches(@"[A-Z]").WithMessage("New password must contain at least one uppercase letter")
                .Matches(@"[a-z]").WithMessage("New password must contain at least one lowercase letter")
                .Matches(@"\d").WithMessage("New password must contain at least one number")
                .Matches(@"[!@#$%^&*(),.?""{}|<>]").WithMessage("New password must contain at least one special character");
        }
    }
}
