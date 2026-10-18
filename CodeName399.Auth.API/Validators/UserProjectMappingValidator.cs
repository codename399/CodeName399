using CodeName399.Auth.API.Models.Dtos;
using DatabaseRepository.Common.Utilities;
using FluentValidation;

namespace CodeName399.Auth.API.Validators
{
    public class UserProjectMappingValidator : AbstractValidator<UserProjectMappingDto>
    {
        public UserProjectMappingValidator()
        {
            RuleFor(x => x.UserId)
                .NotNull()
                .NotEmpty()
                .Must(Util.IsValidGuid);
        }
    }
}
