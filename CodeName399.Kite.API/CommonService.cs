namespace CodeName399.Kite.API
{
    using CodeName399.Kite.API.Constants;
    using CodeName399.Kite.API.Extensions;
    using CodeName399.Kite.API.Helpers;
    using CodeName399.Kite.API.Helpers.Calculators;
    using CodeName399.Kite.API.Models;
    using CodeName399.Kite.API.Models.Enum;
    using CodeName399.Kite.API.Models.Indicator;
    using CodeName399.Kite.API.Services;
    using CodeName399.Kite.API.Services.Interface;
    using Microsoft.Extensions.Options;

    public class CommonService
    {
        private readonly ILogger<CommonService> _logger;

        private readonly AngelOneConfiguration _options;

        private readonly IInstrumentService _instrumentService;

        private readonly IAngelOneService _angel;

        private readonly TradingSettingsProvider _settings;

        private readonly RequestHelper _requestHelper;

        private readonly ITradeOrderFileRepository _tradeOrderFileRepository;

        private readonly OrderStatusCache _orderStatusCache;

        private readonly IServiceScopeFactory _scopeFactory;

        private readonly RawMarketDataArchiveService _rawMarketData;

        private static readonly TimeZoneInfo IndianTimeZone =
    TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows()
            ? "India Standard Time"
            : "Asia/Kolkata");

        private readonly SemaphoreSlim _quoteSemaphore = new(2, 2);

        public CommonService(
            ILogger<CommonService> logger,
            IOptions<AngelOneConfiguration> options,
        IInstrumentService instrumentService,
        IAngelOneService angel,
        TradingSettingsProvider settings,
        RequestHelper requestHelper,
        ITradeOrderFileRepository tradeOrderFileRepository,
        OrderStatusCache orderStatusCache,
        IServiceScopeFactory scopeFactory,
        RawMarketDataArchiveService rawMarketData
        )
        {
            _logger = logger;
            _options = options.Value;
            _instrumentService = instrumentService;
            _angel = angel;
            _settings = settings;
            _requestHelper = requestHelper;
            _tradeOrderFileRepository = tradeOrderFileRepository;
            _orderStatusCache = orderStatusCache;
            _scopeFactory = scopeFactory;
            _rawMarketData = rawMarketData;
        }

        public DateTime GetIndianTime()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow,
                IndianTimeZone);
        }

        public TimeSpan GetDelayUntilNextCandleClose()
        {
            var indianTime = GetIndianTime();

            var nextCandleClose = new DateTime(
                indianTime.Year,
                indianTime.Month,
                indianTime.Day,
                indianTime.Hour,
                indianTime.Minute,
                0,
                DateTimeKind.Unspecified)
                .AddMinutes(1);

            return nextCandleClose - indianTime;
        }

        /// <summary>
        /// Returns symbols that currently have an open position according to either
        /// the file-backed trade repository or completed/partially-filled order updates
        /// received by the order websocket.
        /// </summary>
        public async Task<HashSet<string>> GetOwnedStocksAsync()
        {
            var ownedSymbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // The trade repository is the primary source for trades managed by this application.
            var openTrades = await _tradeOrderFileRepository.GetOpenTradesAsync();
            foreach (var trade in openTrades)
            {
                if (!trade.Closed && !string.IsNullOrWhiteSpace(trade.Symbol))
                    ownedSymbols.Add(trade.Symbol.Trim().ToUpperInvariant());
            }

            // Include positions visible only in order-websocket updates. Calculate net filled
            // quantity per symbol so a completed sell can offset a completed buy.
            var netQuantities = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var order in _orderStatusCache.GetAll())
            {
                if (string.IsNullOrWhiteSpace(order.Symbol) || order.FilledQuantity <= 0)
                    continue;

                var status = order.Status.Trim();
                if (!status.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase)
                    && !status.Equals("PARTIALLY_FILLED", StringComparison.OrdinalIgnoreCase)
                    && !status.Equals("PARTIALLY FILLED", StringComparison.OrdinalIgnoreCase))
                    continue;

                var symbol = order.Symbol.Trim().ToUpperInvariant();
                var sign = order.TransactionType.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? 1
                    : order.TransactionType.Equals("SELL", StringComparison.OrdinalIgnoreCase) ? -1
                    : 0;

                if (sign != 0)
                    netQuantities[symbol] = netQuantities.GetValueOrDefault(symbol) + sign * order.FilledQuantity;
            }

            foreach (var position in netQuantities.Where(x => x.Value > 0))
                ownedSymbols.Add(position.Key);

            return ownedSymbols;
        }

        public long GetActivityVolume(MarketQuoteData quote) =>
    Math.Max(quote.TradeVolume, Math.Max(quote.DayVolume, Math.Max(quote.Volume, quote.LastTradedQuantity)));

        public async Task<List<MarketQuoteData>> GetQuotesAsync(
            List<Instrument> instruments,
            string jwt)
        {
            var settings = _settings.Current;

            var excluded = (settings.ExcludedSymbols ?? [])
                .Select(x => x.ToUpperInvariant())
                .ToHashSet();

            var exchange = string.IsNullOrWhiteSpace(settings.Equity.Exchange)
                ? "NSE"
                : settings.Equity.Exchange.Trim().ToUpperInvariant();

            var quotes = await GetQuotesInChunksAsync(instruments, jwt);

            return quotes;
        }

        private async Task<List<MarketQuoteData>> GetQuotesInChunksAsync(List<Instrument> instruments, string jwt)
        {
            var result = new List<MarketQuoteData>();
            foreach (var group in instruments.GroupBy(x => x.exch_seg))
            {
                foreach (var batch in IndicatorHelper.ChunkTokens(group.Select(x => x.token).ToList()))
                    result.AddRange(await GetBatchAsync(batch, jwt, group.Key));
            }
            return result;
        }

        private async Task<List<MarketQuoteData>> GetBatchAsync(List<string> tokens, string jwt, string exchange)
        {
            await _quoteSemaphore.WaitAsync();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, AngelApiUrlConstant.marketDataUrl);
                request.AddAngelOneHeaders(_options, jwt);
                request.AddContent(new
                {
                    mode = "FULL",
                    exchangeTokens = new Dictionary<string, List<string>> { [exchange] = tokens }
                });

                var result = await _requestHelper.SendAsync<MarketQuoteResponse>(request);
                var data = result.Data?.Fetched ?? [];

                foreach (var quote in data)
                {
                    if (quote.Bid <= 0)
                        quote.Bid = quote.Depth?.Buy?.Where(x => x.Price > 0).Select(x => x.Price).DefaultIfEmpty(0m).Max() ?? 0m;
                    if (quote.Ask <= 0)
                        quote.Ask = quote.Depth?.Sell?.Where(x => x.Price > 0).Select(x => x.Price).DefaultIfEmpty(0m).Min() ?? 0m;

                    quote.InstrumentType = InstrumentType.Equity;
                    quote.Exchange = exchange;
                    quote.QuoteTimeUtc = DateTime.UtcNow;
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Equity quote request failed for {Exchange}", exchange);
                return [];
            }
            finally
            {
                _quoteSemaphore.Release();
            }
        }

        public List<MarketQuoteData> GetTopQuotes(List<MarketQuoteData> quotes, HashSet<string> ownedStocks, int count)
        {
            return quotes
                .Where(x => !ownedStocks.Contains(x.SymbolToken))
                .Where(x => x.Ltp > 0m && x.Close > 0m && x.Ltp > x.Close)
            .Where(x => GetActivityVolume(x) > 0L)
            .OrderByDescending(GetActivityVolume)
            .ThenByDescending(x => (x.Ltp - x.Close) / x.Close)
            .ThenByDescending(x => (x.Ltp - x.Open) / x.Open)
            .Take(count)
            .ToList();
        }

        /// <summary>
        /// Downloads candles for the supplied quotes by calling DownloadCandleAsync
        /// for at most three instrument tokens concurrently per batch.
        /// </summary>
        public async Task<Dictionary<string, List<Candle>>> DownloadCandlesInBatchesAsync(
            List<MarketQuoteData> quotes,
            List<Instrument> instruments,
            string jwtToken,
            string interval,
            DateTime fromDate,
            DateTime toDate,
            int batchSize = 2,
            int batchDelay = 1)
        {
            var candlesByToken = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);

            if (quotes == null || quotes.Count == 0 || instruments == null || instruments.Count == 0)
                return candlesByToken;

            if (string.IsNullOrWhiteSpace(jwtToken))
                throw new ArgumentException("JWT token is required to download historical candles.", nameof(jwtToken));

            batchSize = Math.Max(1, Math.Min(batchSize, 3));

            var instrumentsByToken = instruments
                .Where(x => !string.IsNullOrWhiteSpace(x.token))
                .GroupBy(x => x.token.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var requestedTokens = quotes
                .Where(q => !string.IsNullOrWhiteSpace(q.SymbolToken))
                .Select(q => q.SymbolToken.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(instrumentsByToken.ContainsKey)
                .ToList();

            for (var offset = 0; offset < requestedTokens.Count; offset += batchSize)
            {
                var batch = requestedTokens.Skip(offset).Take(batchSize).ToList();

                var tasks = batch.Select(async token =>
                {
                    var instrument = instrumentsByToken[token];
                    var exchange = string.IsNullOrWhiteSpace(instrument.exch_seg)
                        ? "NSE"
                        : instrument.exch_seg.Trim().ToUpperInvariant();

                    try
                    {
                        var candles = await DownloadCandleAsync(
                            token,
                            jwtToken,
                            interval,
                            fromDate,
                            toDate,
                            exchange);

                        return (Token: token, Candles: candles ?? new List<Candle>());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to download {Interval} candles for {Symbol} (token {Token})",
                            interval,
                            instrument.symbol,
                            token);

                        return (Token: token, Candles: new List<Candle>());
                    }
                });

                var batchResults = await Task.WhenAll(tasks);
                foreach (var result in batchResults)
                    candlesByToken[result.Token] = result.Candles;

                await Task.Delay(TimeSpan.FromSeconds(batchDelay));
            }

            _logger.LogInformation(
                "Downloaded {LoadedCount} of {RequestedCount} stocks with {Interval} candles in batches of {BatchSize}",
                candlesByToken.Count(x => x.Value.Count > 0),
                requestedTokens.Count,
                interval,
                batchSize);

            return candlesByToken;
        }

        public async Task<List<Candle>> DownloadCandleAsync(
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
