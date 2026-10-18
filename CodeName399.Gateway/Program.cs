using DatabaseRepository.Constants;
using DatabaseRepository.Extensions;
using DatabaseRepository.Helper;
using Ocelot.DependencyInjection;
using Ocelot.Middleware;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

// Load production settings when the application is running as a Windows service.
if (Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService())
{
    builder.Configuration.AddJsonFile(
        "appsettings.Production.json",
        optional: true,
        reloadOnChange: true);
}

builder.Host.UseWindowsService();

// IMPORTANT:
// Do not use the Windows EventLog logging provider here.
// The server was running the .NET 8 Gateway while an incompatible
// System.Threading.AccessControl 10.x assembly was being requested
// from the EventLog logging path. That caused the startup exception
// itself to become an AggregateException and left port 5000 unusable.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Configuration.AddJsonFile(
    "ocelot.json",
    optional: false,
    reloadOnChange: true);

AppSettingsHelper.Initialize(builder.Configuration);

builder.Services.AddJwtAuthentication(
    BaseConstant.PasswordBasedAuthenticationConfig);

builder.Services.AddOcelot(builder.Configuration);
builder.Services.AddSwaggerService();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAngularApp",
        policy => policy
            .WithOrigins(
                "http://localhost:4200",
                "https://codename399.com",
                "https://api.codename399.com",
                "http://192.168.1.103",
                "http://192.168.1.103:80")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services
    .AddControllers()
    .AddJsonOptions(opts =>
    {
        opts.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter());
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowAngularApp");
app.UseWebSockets();

await app.UseOcelot();

app.Run();
