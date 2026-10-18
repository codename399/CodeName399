using AutoMapper;
using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Models.Dtos;

namespace CodeName399.Auth.API.MappingProfile
{
    public class MappingProfile : Profile
    {
        public MappingProfile()
        {
            CreateMap<User, UserDto>();
            CreateMap<UserDto, User>();
        }
    }
}
