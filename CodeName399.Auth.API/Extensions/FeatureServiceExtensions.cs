using CodeName399.Auth.API.Constants;
using CodeName399.Auth.API.Model;
using CodeName399.Auth.API.Models;
using CodeName399.Auth.API.Models.Dtos;
using CodeName399.Auth.API.Repositories;
using CodeName399.Auth.API.Repositories.Interface;
using CodeName399.Auth.API.Repository;
using CodeName399.Auth.API.Repository.Interface;
using CodeName399.Auth.API.Service;
using CodeName399.Auth.API.Service.Interface;
using CodeName399.Auth.API.Services;
using CodeName399.Auth.API.Services.Interface;
using CodeName399.Auth.API.Validators;
using CodeName399.Model;
using DatabaseRepository.Constants;
using DatabaseRepository.Model;
using DatabaseRepository.Validators;
using FluentValidation;

namespace CodeName399.Extensions
{
    public static class FeatureServiceExtensions
    {
        public static void AddServiceDependencies(this IServiceCollection services)
        {
            services.AddSingleton<IUserService, UserService>();
            services.AddSingleton<IRoleService, RoleService>();
            services.AddSingleton<IProjectService, ProjectService>();
            services.AddSingleton<IUserProjectMappingService, UserProjectMappingService>();
            services.AddSingleton<IRefreshTokenService, RefreshTokenService>();
        }

        public static void AddRepositoryDependencies(this IServiceCollection services)
        {
            services.AddSingleton<IUserRepository, UserRepository>();
            services.AddSingleton<IRoleRepository, RoleRepository>();
            services.AddSingleton<IProjectRepository, ProjectRepository>();
            services.AddSingleton<IUserProjectMappingRepository, UserProjectMappingRepository>();
            services.AddSingleton<IRefreshTokenRepository, RefreshTokenRepository>();
        }

        public static void AddConfigurationDependencies(this IServiceCollection services, WebApplicationBuilder builder)
        {
            services.Configure<AuthenticationDatabaseSetting>(builder.Configuration.GetSection(BaseConstant.MongoDatabaseSetting));
            services.Configure<AuthenticationConfig>(builder.Configuration.GetSection(BaseConstant.PasswordBasedAuthenticationConfig));
            services.Configure<AuthenticationConfig>(builder.Configuration.GetSection(BaseConstant.OTPBasedAuthenticationConfig));
        }

        public static void AddFluentValidations(this IServiceCollection services)
        {
            services.AddScoped<IValidator<User>, UserValidator>();
            services.AddScoped<IValidator<Role>, RoleValidator>();
            services.AddScoped<IValidator<Project>, ProjectValidator>();
            services.AddScoped<IValidator<ChangePasswordRequest>, ChangePasswordRequestValidator>();
            services.AddScoped<IValidator<string>, IdValidator>();
            services.AddScoped<IValidator<LoginUserRequest>, LoginRequestValidator>();
            services.AddScoped<IValidator<UserProjectMappingDto>, UserProjectMappingValidator>();
        }

        public static void AddAuthorizationPolicies(this IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy(PolicyConstant.AuthenticationAdmin, policy =>
                {
                    policy.RequireRole(BaseConstant.Admin);
                });
            });
        }
    }
}
