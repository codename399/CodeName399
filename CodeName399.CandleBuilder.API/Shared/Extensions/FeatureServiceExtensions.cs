using CodeName399.CandleBuilder.API.Application.BackgroundServices;
using CodeName399.CandleBuilder.API.Constants;
using CodeName399.CandleBuilder.API.Helpers;
using CodeName399.CandleBuilder.API.Models;
using CodeName399.CandleBuilder.API.Services;
using CodeName399.CandleBuilder.API.Services.Interface;
using CodeName399.CandleBuilder.API.WebSocket.Interface;

namespace CodeName399.Extensions
{
    public static class FeatureServiceExtensions
    {
        public static void AddServiceDependencies(this IServiceCollection services)
        {
            services.AddHttpClient(
                "AngelRefresh",
                client =>
                {
                    client.BaseAddress = new Uri(AngelApiUrlConstant.baseUrl);
                }
            );
        }

        public static void AddSingletonServiceDependencies(this IServiceCollection services)
        {
            services.AddSingleton<RawMarketDataArchiveService>();
            services.AddSingleton<LiveCandleEngine>();
            services.AddSingleton<AngelBinaryParser>();
            services.AddSingleton<IAngelWebSocketClient, AngelWebSocketClient>();
            services.AddSingleton<RequestHelper>();
            services.AddSingleton<MarketClock>();
        }

        public static void AddScopedServiceDependencies(this IServiceCollection services)
        {
            services.AddSingleton<IInstrumentService, InstrumentService>();
        }

        public static void AddTransientServiceDependencies(this IServiceCollection services)
        {
            services.AddTransient<IAngelWebSocketConnection, AngelWebSocketConnection>();
        }

        public static void AddHostedServiceDependencies(this IServiceCollection services)
        {
            services.AddSignalR();
            services.AddHostedService<RawMarketDataArchiveHostedService>();
            services.AddHostedService(sp => sp.GetRequiredService<LiveCandleEngine>());
            services.AddHostedService<AngelWebSocketBackgroundService>();
        }

        public static void AddConfigurationDependencies(
            this IServiceCollection services,
            WebApplicationBuilder builder
        )
        {
            services.Configure<AngelOneConfiguration>(
                builder.Configuration.GetSection("AngelOne")
            );
        }
    }
}
