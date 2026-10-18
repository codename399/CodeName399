using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Extensions;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;
using Microsoft.Extensions.Options;

namespace CodeName399.Kite.API.Services.Indicator
{
    public class HistoricalDataService : IHistoricalDataService
    {
        private readonly AngelOneConfiguration _options;

        private ILogger<HistoricalDataService> _logger;

        private readonly RequestHelper _requestHelper;

        private readonly HistoricalRateLimiter _rateLimiter;
        private readonly MarketClock _clock;
        private readonly RawMarketDataArchiveService _rawMarketData;

        public HistoricalDataService(
            IOptions<AngelOneConfiguration> options,
            ILogger<HistoricalDataService> logger,
            RequestHelper requestHelper,
            HistoricalRateLimiter rateLimiter,
            MarketClock clock,
            RawMarketDataArchiveService rawMarketData
        )
        {
            _options = options.Value;

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
            // Deliberately no in-memory historical candle cache: the UI requests
            // candles explicitly and continues building the current candle from ticks.
            return await LoadAsync(token, jwtToken, interval, exchange);
        }

        private async Task<List<Candle>> LoadAsync(string token, string jwtToken, string interval, string exchange = "NSE")
        {
            return await _rateLimiter.ExecuteAsync(
                async () =>
                {
                    DateTime toDate = _clock.Now;
                    DateTime fromDate = _clock.Today.Date;
                    return await DownloadAsync(token, jwtToken, interval, fromDate, toDate, exchange);
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
