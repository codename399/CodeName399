using CodeName399.Auth.API.Model;
using FluentValidation;

namespace CodeName399.Auth.API.Validators
{
    public class UserValidator : AbstractValidator<User>
    {
        public UserValidator()
        {
            RuleFor(x => x.Name)
                .NotNull()
                .NotEmpty();

            RuleFor(x => x.Username)
                .NotNull()
                .NotEmpty();

            RuleFor(x => x.Password)
                .NotNull()
                .NotEmpty()
                .MinimumLength(8).WithMessage("Password must be at least 8 characters long")
                .Matches(@"[A-Z]").WithMessage("Password must contain at least one uppercase letter")
                .Matches(@"[a-z]").WithMessage("Password must contain at least one lowercase letter")
                .Matches(@"\d").WithMessage("Password must contain at least one number")
                .Matches(@"[!@#$%^&*(),.?""{}|<>]").WithMessage("Password must contain at least one special character")
                .When(x => x.Id is null, ApplyConditionTo.AllValidators);

            RuleFor(x => x.EmailId)
                .NotNull()
                .NotEmpty()
                .EmailAddress();

            RuleFor(x => x.ContactNumber)
                .NotNull()
                .NotEmpty()
                .Matches(@"^[0-9]{10}$").WithMessage("Invalid Indian mobile number format");
        }
    }
}
