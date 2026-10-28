using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Extensions;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CodeName399.Kite.API.Services.Indicator
{
    public class HistoricalDataService : IHistoricalDataService
    {
        private readonly HttpClient _httpClient;

        private readonly AngelOneConfiguration _options;

        private readonly IMemoryCache _cache;

        private static readonly Dictionary<string, TimeSpan> CacheDurations = new()
        {
            ["ONE_MINUTE"] = TimeSpan.FromMinutes(1),
            ["FIVE_MINUTE"] = TimeSpan.FromMinutes(5),
            ["FIFTEEN_MINUTE"] = TimeSpan.FromMinutes(15),
            ["THIRTY_MINUTE"] = TimeSpan.FromMinutes(30),
            ["ONE_HOUR"] = TimeSpan.FromHours(1),
            ["ONE_DAY"] = TimeSpan.FromHours(12),
        };

        private ILogger<HistoricalDataService> _logger;

        private readonly RequestHelper _requestHelper;

        private readonly HistoricalRateLimiter _rateLimiter;
        private readonly MarketClock _clock;
        private readonly RawMarketDataArchiveService _rawMarketData;

        public HistoricalDataService(
            IHttpClientFactory factory,
            IOptions<AngelOneConfiguration> options,
            IMemoryCache cache,
            ILogger<HistoricalDataService> logger,
            RequestHelper requestHelper,
            HistoricalRateLimiter rateLimiter,
            MarketClock clock,
            RawMarketDataArchiveService rawMarketData
        )
        {
            _httpClient = factory.CreateClient(Constant.AngelRefresh);

            _options = options.Value;

            _cache = cache;

            _logger = logger;

            _requestHelper = requestHelper;

            _rateLimiter = rateLimiter;
            _clock = clock;
            _rawMarketData = rawMarketData;
        }

        public async Task<List<Candle>> GetAsync(
            string token,
            string jwtToken,
            string interval = "FIVE_MINUTE",
            string exchange = "NSE"
        )
        {
            string cacheKey = $"HIST_{exchange}_{token}_{interval}";

            if (_cache.TryGetValue(cacheKey, out List<Candle> candles))
            {
                if (candles.Count > 0)
                    return candles;
            }

            candles = await LoadAsync(token, jwtToken, interval, exchange);

            var duration = CacheDurations.TryGetValue(interval, out var cacheTime)
                ? cacheTime
                : TimeSpan.FromMinutes(5);

            _cache.Set(cacheKey, candles, duration);

            return candles;
        }

        private async Task<List<Candle>> LoadAsync(string token, string jwtToken, string interval, string exchange = "NSE")
        {
            return await _rateLimiter.ExecuteAsync(
                async () =>
                {
                    DateTime toDate = _clock.Now;

                    DateTime fromDate = interval switch
                    {
                        "ONE_MINUTE" => toDate.AddDays(-3),
                        "FIVE_MINUTE" => toDate.AddDays(-7),
                        "FIFTEEN_MINUTE" => toDate.AddDays(-20),
                        "THIRTY_MINUTE" => toDate.AddDays(-40),
                        "ONE_HOUR" => toDate.AddDays(-90),
                        "ONE_DAY" => toDate.AddYears(-1),
                        _ => toDate.AddDays(-7),
                    };

                    return await DownloadAsync(token, jwtToken, interval, fromDate, toDate, exchange);
                },
                _logger
            );
        }

        public async Task<List<Candle>> RefreshAsync(
            string token,
            string jwtToken,
            string interval,
            List<Candle> existing,
            string exchange = "NSE"
        )
        {
            if (existing == null || existing.Count == 0)
                return await LoadAsync(token, jwtToken, interval, exchange);

            var latest = existing.Last().Timestamp;

            //
            // Expected interval between completed candles.
            //
            var expectedGap = interval switch
            {
                "ONE_MINUTE" => TimeSpan.FromMinutes(1),
                "FIVE_MINUTE" => TimeSpan.FromMinutes(5),
                "FIFTEEN_MINUTE" => TimeSpan.FromMinutes(15),
                "THIRTY_MINUTE" => TimeSpan.FromMinutes(30),
                "ONE_HOUR" => TimeSpan.FromHours(1),
                _ => TimeSpan.FromMinutes(1),
            };

            //
            // No completed candle is expected yet.
            //
            if (_clock.Now - latest < expectedGap)
                return existing;

            var fresh = await LoadIncrementalAsync(token, jwtToken, interval, latest, exchange);

            if (fresh.Count == 0)
                return existing;

            return existing
                .Concat(fresh)
                .GroupBy(x => x.Timestamp)
                .Select(g => g.Last())
                .OrderBy(x => x.Timestamp)
                .TakeLast(500)
                .ToList();
        }

        private async Task<List<Candle>> LoadIncrementalAsync(
            string token,
            string jwtToken,
            string interval,
            DateTime lastCandle,
            string exchange = "NSE"
        )
        {
            return await _rateLimiter.ExecuteAsync(
                async () =>
                {
                    DateTime fromDate = interval switch
                    {
                        "ONE_MINUTE" => lastCandle.AddMinutes(-1),
                        "FIVE_MINUTE" => lastCandle.AddMinutes(-5),
                        "FIFTEEN_MINUTE" => lastCandle.AddMinutes(-15),
                        "THIRTY_MINUTE" => lastCandle.AddMinutes(-30),
                        "ONE_HOUR" => lastCandle.AddHours(-1),
                        _ => lastCandle.AddMinutes(-1),
                    };

                    return await DownloadAsync(token, jwtToken, interval, fromDate, _clock.Now, exchange);
                },
                _logger
            );
        }

        private async Task<List<Candle>> DownloadAsync(
            string token,
            string jwtToken,
            string interval,
            DateTime fromDate,
            DateTime toDate,
            string exchange = "NSE"
        )
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                AngelApiUrlConstant.historicalDataUrl
            );

            request.AddAngelOneHeaders(_options, jwtToken);

            request.AddContent(
                new
                {
                    exchange = exchange,
                    symboltoken = token,
                    interval,
                    fromdate = fromDate.ToString("yyyy-MM-dd HH:mm"),
                    todate = toDate.ToString("yyyy-MM-dd HH:mm"),
                }
            );

            try
            {
                var result = await _requestHelper.SendAsync<HistoricalDataResponse>(request);

                if (!result.Status)
                {
                    _logger.LogError(
                        "Historical data request failed. Token={Token}, Interval={Interval}, ErrorCode={ErrorCode}, Message={Message}",
                        token,
                        interval,
                        result.ErrorCode,
                        result.Message
                    );

                    return [];
                }

                if (result.Data == null || result.Data.Count == 0)
                {
                    _logger.LogDebug(
                        "No historical candles returned. Token={Token}, Interval={Interval}",
                        token,
                        interval
                    );

                    return [];
                }

                var candles = result
                    .Data.Where(x => x.Count >= 6)
                    .Select(x =>
                    {
                        try
                        {
                            return new Candle
                            {
                                Timestamp = x[0].GetDateTime(),
                                Open = x[1].GetDecimal(),
                                High = x[2].GetDecimal(),
                                Low = x[3].GetDecimal(),
                                Close = x[4].GetDecimal(),
                                Volume = x[5].GetDecimal(),
                            };
                        }
                        catch
                        {
                            return null;
                        }
                    })
                    .Where(c => c != null)
                    .Where(c =>
                        c!.Open > 0
                        && c.High > 0
                        && c.Low > 0
                        && c.Close > 0
                        && c.High >= c.Low
                        && c.Volume >= 0
                    )
                    .GroupBy(c => c!.Timestamp)
                    .Select(g => g.Last()!)
                    .OrderBy(c => c.Timestamp)
                    .ToList();

                if (candles.Count == 0)
                {
                    _logger.LogDebug(
                        "No valid historical candles after filtering. Token={Token}, Interval={Interval}",
                        token,
                        interval
                    );

                    return [];
                }

                try
                {
                    await _rawMarketData.RecordHistoricalCandlesAsync(token, exchange, interval, candles);
                }
                catch (Exception archiveEx)
                {
                    _logger.LogWarning(archiveEx, "Failed to archive raw historical candles. Token={Token}, Interval={Interval}", token, interval);
                }

                _logger.LogDebug(
                    "Downloaded {Count} candles. Token={Token}, Interval={Interval}, From={From}, To={To}",
                    candles.Count,
                    token,
                    interval,
                    candles.First().Timestamp,
                    candles.Last().Timestamp
                );

                return candles;
            }
            catch (AngelApiException ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to download historical data. Token={Token}, Interval={Interval}, Status={Status}, Response={Response}",
                    token,
                    interval,
                    ex.StatusCode,
                    ex.Response
                );

                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Unexpected error downloading historical data. Token={Token}, Interval={Interval}",
                    token,
                    interval
                );

                return [];
            }
        }
    }
}
