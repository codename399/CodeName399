using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Extensions;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Services.Interface;
using Microsoft.Extensions.Options;

namespace CodeName399.Kite.API.Services
{
    public class PortfolioService : IPortfolioService
    {
        private readonly HttpClient _httpClient;

        private readonly AngelOneConfiguration _options;

        private readonly IAngelOneService _angelOneService;

        protected readonly TradingSettingsProvider _tradingSettingsProvider;
        private readonly RequestHelper _requestHelper;
        private readonly ILogger<PortfolioService> _logger;
        private readonly SmartApiRateLimiter _rateLimiter;

        public PortfolioService(
            IHttpClientFactory factory,
            IOptions<AngelOneConfiguration> options,
            IAngelOneService angelOneService,
            TradingSettingsProvider tradingSettingsProvider,
            RequestHelper requestHelper,
            ILogger<PortfolioService> logger,
            SmartApiRateLimiter rateLimiter
        )
        {
            _httpClient = factory.CreateClient(Constant.AngelRefresh);

            _options = options.Value;

            _angelOneService = angelOneService;

            _tradingSettingsProvider = tradingSettingsProvider;

            _requestHelper = requestHelper;

            _logger = logger;
            _rateLimiter = rateLimiter;
        }

        public async Task<HashSet<string>> GetOwnedSymbolsAsync(string jwtToken)
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                AngelApiUrlConstant.holdingsUrl
            );

            request.AddAngelOneHeaders(_options, jwtToken);
            await _rateLimiter.WaitAsync("holdings");

            try
            {
                var result = await _requestHelper.SendAsync<HoldingsResponse>(request);

                if (result.Data == null)
                {
                    return [];
                }

                var excluded =
                    _tradingSettingsProvider
                        .Current.ExcludedSymbols?.Select(x => x.Trim().ToUpperInvariant())
                        .ToHashSet()
                    ?? [];

                return result
                    .Data.Where(x =>
                        x.Quantity > 0 && !excluded.Contains(x.TradingSymbol.ToUpperInvariant())
                    )
                    .Select(x => x.TradingSymbol)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch (AngelApiException ex)
            {
                _logger.LogWarning(
                    "Unable to fetch holdings. Status={Status} Response={Response}",
                    ex.StatusCode,
                    ex.Response
                );

                return [];
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error while fetching holdings.");

                return [];
            }
        }

        public async Task<HashSet<string>> GetOwnedSymbolsAsync()
        {
            var login = await _angelOneService.GetLoginDataAsync();

            return await GetOwnedSymbolsAsync(login.JwtToken);
        }
    }
}
