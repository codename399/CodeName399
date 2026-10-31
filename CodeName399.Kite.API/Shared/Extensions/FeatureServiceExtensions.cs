using CodeName399.Kite.API;
using CodeName399.Kite.API.BackgroundServices;
using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Indicators.Calculators;
using CodeName399.Kite.API.Indicators.Engine;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Repositories;
using CodeName399.Kite.API.Repositories.Interface;
using CodeName399.Kite.API.Repository;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Brokerage;
using CodeName399.Kite.API.Services.Indicator;
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
            services.AddSingleton<TradingStateService>();
            services.AddSingleton<RawMarketDataArchiveService>();
            services.AddSingleton<TradingSettingsProvider>();
            services.AddSingleton<LiveMarketCache>();
            services.AddSingleton<AngelBinaryParser>();
            services.AddSingleton<IMarketDataProvider, MarketDataProvider>();
            services.AddSingleton<WatchListService>();
            services.AddSingleton<HistoricalCacheService>();
            services.AddSingleton<IAngelWebSocketClient, AngelWebSocketClient>();
            services.AddSingleton<IMarketHoursService, MarketHoursService>();
            services.AddSingleton<IBrokerageCalculatorService, BrokerageCalculatorService>();
            services.AddSingleton<OrderStatusCache>();
            services.AddSingleton<IAngelOrderWebSocket, AngelOrderWebSocket>();
            services.AddSingleton<HistoricalDownloadQueue>();
            services.AddSingleton<TradeManager>();
            services.AddSingleton<SymbolLockService>();
            services.AddSingleton<RequestHelper>();
            services.AddSingleton<AtrIndicator>();
            services.AddSingleton<IndicatorEngine>();
            services.AddSingleton<HistoricalRateLimiter>();
            services.AddSingleton<SmartApiRateLimiter>();
            services.AddSingleton<IntradayRiskGuard>();
            services.AddSingleton<MarketClock>();
            services.AddSingleton<EmaCrossoverStrategyService>();
            services.AddSingleton<PaperPortfolioService>();
            services.AddSingleton<CasEligibilityService>();
            services.AddSingleton<CasTradingRulesService>();
            services.AddSingleton<CommonService>();
        }

        public static void AddScopedServiceDependencies(this IServiceCollection services)
        {
            services.AddSingleton<IInstrumentService, InstrumentService>();
            services.AddScoped<IAngelMarketService, AngelMarketService>();
            services.AddScoped<IPortfolioService, PortfolioService>();
            services.AddScoped<DashboardService>();
            services.AddScoped<IHistoricalDataService, HistoricalDataService>();
            services.AddScoped<MarketScannerService>();
            services.AddScoped<IIndicatorService, IndicatorService>();
            services.AddScoped<TradeValidationService>();
            services.AddScoped<BuyTradingService>();
            services.AddScoped<SellTradingService>();
            services.AddScoped<SquareOffService>();
            services.AddScoped<IDailySummaryService, DailySummaryService>();
            services.AddScoped<BrokerReconciliationService>();
        }

        public static void AddTransientServiceDependencies(this IServiceCollection services)
        {
            services.AddTransient<IAngelWebSocketConnection, AngelWebSocketConnection>();
        }

        public static void AddHostedServiceDependencies(this IServiceCollection services)
        {
            services.AddSignalR();
            services.AddHostedService<RawMarketDataArchiveHostedService>();
            services.AddHostedService<AngelWebSocketBackgroundService>();
            services.AddHostedService<MarketDiscoveryBackgroundService>();
            services.AddHostedService<OrderWebSocketBackgroundService>();
            services.AddHostedService<BuyTradingBackgroundService>();
            services.AddHostedService<SellTradingBackgroundService>();
            services.AddHostedService<BrokerReconciliationBackgroundService>();
            services.AddHostedService<HistoricalCacheBackgroundService>();
            services.AddHostedService<TradeInitializationHostedService>();
            services.AddHostedService<TradingSchedulerBackgroundService>();
            services.AddHostedService<TradingTickBackgroundService>();
            services.AddHostedService<ExitRecoveryBackgroundService>();
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
