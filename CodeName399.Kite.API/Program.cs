using CodeName399.Extensions;
using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Json;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.SignalRHubs;
using DatabaseRepository.Common.Middlewares;
using DatabaseRepository.Constants;
using DatabaseRepository.Extensions;
using DatabaseRepository.Helper;
using Serilog;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Load deployment secrets from environment variables prefixed with CODENAME399_.
// Nested configuration keys use double underscores, e.g.
// CODENAME399_AngelOne__ApiKey and CODENAME399_MongoDatabaseSetting__LocalConnectionString.
builder.Configuration.AddEnvironmentVariables("CODENAME399_");

// CodeName399.Kite must listen on TCP 5004.
// Do not rely on launchSettings.json for the published Windows service.
builder.WebHost.UseUrls("http://127.0.0.1:5004");
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    builder.Configuration.AddJsonFile("appsettings.Production.json", optional: true, reloadOnChange: true);
}
builder.Host.UseWindowsService();

//Adding to get the configuration settings from appsettings.json

AppSettingsHelper.Initialize(builder.Configuration);
EmailHelper.Initialize(builder.Configuration);

// Add services to the container.
builder.Services.AddConfigurationDependencies(builder);
builder.Services.AddAutoMapperServiceDependencies();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerService();
builder.Services.AddServiceDependencies();
builder.Services.AddSingletonServiceDependencies();
builder.Services.AddScopedServiceDependencies();
builder.Services.AddTransientServiceDependencies();
builder.Services.AddHostedServiceDependencies();
builder.Services.AddRepositoryDependencies();
builder.Services.AddHttpClient();
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();

// Configure Serilog
var logDirectory = Path.Combine(builder.Environment.ContentRootPath, "Logs");

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.Sink(new SelfHealingRollingFileSink(logDirectory))
    .Enrich.FromLogContext()
    .Enrich.With<TradingActivityEnricher>()
    .MinimumLevel.Information()
    .CreateLogger();

// Ensure today's log file exists immediately and can be recreated after deletion.
Log.Information("Application logging initialized. LogDirectory={LogDirectory}", logDirectory);

// Replace default logging
builder.Host.UseSerilog();

//Adding to fix CORS policy issue
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp",
           policy => policy.WithOrigins("http://localhost:4200",
                                        "https://codename399.com",
                                        "http://192.168.1.103",
                                        "http://192.168.1.103:80")
                           .AllowAnyHeader()
                           .AllowAnyMethod()
                           .AllowCredentials());
});

//Adding to accept enum values as string
builder.Services
    .AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        opts.JsonSerializerOptions.Converters.Add(new FlexibleTimeSpanJsonConverter());
        // Accept decimal values emitted by JavaScript, including rounded decimal.MaxValue sent as a JSON number.
        opts.JsonSerializerOptions.Converters.Add(new FlexibleDecimalConverter());
    });

builder.Services.AddJwtAuthentication(BaseConstant.PasswordBasedAuthenticationConfig);

builder.Services.AddAuthorizationPolicies();

var app = builder.Build();

// Fail fast on invalid exit/analytics/session configuration rather than allowing
// a malformed runtime configuration to produce silent trading behaviour.
TradingConfigurationValidator.Validate(app.Services.GetRequiredService<TradingSettingsProvider>().Current);

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

app.MapHub<MarketHub>("/marketHub");

app.MapControllers();

app.Run();