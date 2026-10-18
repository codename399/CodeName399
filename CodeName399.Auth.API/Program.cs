using CodeName399.Extensions;
using DatabaseRepository.Common.Middlewares;
using DatabaseRepository.Constants;
using DatabaseRepository.Extensions;
using DatabaseRepository.Helper;
using Serilog;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    builder.Configuration.AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: true);
}
builder.Host.UseWindowsService();

//Adding to get the configuration settings from appsettings.json

AppSettingsHelper.Initialize(builder.Configuration);

// Add services to the container.

builder.Services.AddAutoMapperServiceDependencies();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerService();
builder.Services.AddServiceDependencies();
builder.Services.AddRepositoryDependencies();
builder.Services.AddConfigurationDependencies(builder);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("Logs/log-.txt", rollingInterval: RollingInterval.Day)
    .Enrich.FromLogContext()
    .MinimumLevel.Information()
    .CreateLogger();

// Replace default logging
builder.Host.UseSerilog();

//Adding to fix CORS policy issue
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp",
           policy => policy.WithOrigins("http://localhost:4200",
                                        "https://codename399.com",
                                        "https://api.codename399.com",
                                        "http://192.168.1.103",
                                        "http://192.168.1.103:80")
                           .AllowAnyHeader()
                           .AllowAnyMethod());
});

//Adding to accept enum values as string
builder.Services
    .AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

builder.Services.AddFluentValidations();

builder.Services.AddJwtAuthentication(BaseConstant.PasswordBasedAuthenticationConfig);

builder.Services.AddAuthorizationPolicies();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

app.UseCors("AllowAngularApp");

app.UseAuthentication();

app.UseAuthorization();

app.MapControllers();

app.Run();
