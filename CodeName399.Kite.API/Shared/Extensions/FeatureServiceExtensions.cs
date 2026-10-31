using CodeName399.Kite.API.Application.BackgroundServices;
using CodeName399.Kite.API.Application.Services;
using CodeName399.Kite.API.BackgroundServices;
using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Repositories;
using CodeName399.Kite.API.Repositories.Interface;
using CodeName399.Kite.API.Repository;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Brokerage;
using CodeName399.Kite.API.Services.Interface;
using CodeName399.Kite.API.WebSocket.Interface;
using CodeName399.Model;
using DatabaseRepository.Authorization.Handlers;
using DatabaseRepository.Authorization.Requirements;
using DatabaseRepository.Constants;
using DatabaseRepository.Model;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace CodeName399.Extensions
{
    public static class FeatureServiceExtensions
    {
        public static void AddServiceDependencies(this IServiceCollection services)
        {
            services.AddHttpClient(
                Constant.AngelRefresh,
                client =>
                {
                    client.BaseAddress = new Uri(AngelApiUrlConstant.baseUrl);
                }
            );

            services.AddSingleton<IMongoClient>(sp =>
            {
                var config = sp.GetRequiredService<IOptions<KiteDatabaseSetting>>().Value;

                var settings = MongoClientSettings.FromConnectionString(
                    config.LocalConnectionString
                );

                settings.MaxConnectionPoolSize = 300;
                settings.MinConnectionPoolSize = 20;
                settings.MaxConnecting = 10;
                settings.WaitQueueTimeout = TimeSpan.FromSeconds(30);
                settings.ConnectTimeout = TimeSpan.FromSeconds(10);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);

                return new MongoClient(settings);
            });
        }

        public static void AddSingletonServiceDependencies(this IServiceCollection services)
        {
            services.AddSingleton<IAngelOneService, AngelOneService>();
            services.AddSingleton<RawMarketDataArchiveService>();
            services.AddSingleton<TradingSettingsProvider>();
            services.AddSingleton<AngelBinaryParser>();
            services.AddSingleton<IAngelWebSocketClient, AngelWebSocketClient>();
            services.AddSingleton<IMarketHoursService, MarketHoursService>();
            services.AddSingleton<IBrokerageCalculatorService, BrokerageCalculatorService>();
            services.AddSingleton<OrderStatusCache>();
            services.AddSingleton<IAngelOrderWebSocket, AngelOrderWebSocket>();
            services.AddSingleton<RequestHelper>();
            services.AddSingleton<MarketClock>();
            services.AddSingleton<PaperPortfolioService>();
            services.AddSingleton<CommonService>();
            services.AddSingleton<MarketQuotePublisher>();
        }

        public static void AddScopedServiceDependencies(this IServiceCollection services)
        {
            services.AddSingleton<IInstrumentService, InstrumentService>();
            services.AddScoped<DashboardService>();
            services.AddScoped<SquareOffService>();
            services.AddScoped<IDailySummaryService, DailySummaryService>();
        }

        public static void AddTransientServiceDependencies(this IServiceCollection services)
        {
            services.AddTransient<IAngelWebSocketConnection, AngelWebSocketConnection>();
        }

        public static void AddHostedServiceDependencies(this IServiceCollection services)
        {
            services.AddSignalR();
            services.AddHostedService<RawMarketDataArchiveHostedService>();
            services.AddHostedService<MarketQuotePublisher>(sp => sp.GetRequiredService<MarketQuotePublisher>());
            services.AddHostedService<AngelWebSocketBackgroundService>();
            services.AddHostedService<OrderWebSocketBackgroundService>();
            services.AddHostedService<InitialBackgroundService>();
            services.AddHostedService<ExitBackgroundService>();
        }

        public static void AddRepositoryDependencies(this IServiceCollection services)
        {
            services.AddSingleton<ITradeOrderFileRepository, TradeOrderFileRepository>();
            services.AddSingleton<TradeCloseHistoryStore>();
            services.AddSingleton<
                ITradingConfigurationRepository,
                TradingConfigurationRepository
            >();
        }

        public static void AddConfigurationDependencies(
            this IServiceCollection services,
            WebApplicationBuilder builder
        )
        {
            services.Configure<AuthenticationConfig>(
                builder.Configuration.GetSection(BaseConstant.PasswordBasedAuthenticationConfig)
            );
            services.Configure<AngelOneConfiguration>(
                builder.Configuration.GetSection(Constant.AngelOne)
            );
            services.Configure<KiteDatabaseSetting>(
                builder.Configuration.GetSection(BaseConstant.MongoDatabaseSetting)
            );
        }

        public static void AddAuthorizationPolicies(this IServiceCollection services)
        {
            services.AddAuthorization(options =>
            {
                options.AddPolicy(
                    PolicyConstant.KitePolicy,
                    policy =>
                    {
                        policy.Requirements.Add(new ProjectNameRequirement(Constant.Kite));
                    }
                );
            });

            services.AddSingleton<IAuthorizationHandler, ProjectNameHandler>();
        }
    }
}
