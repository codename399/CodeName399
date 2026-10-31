namespace CodeName399.Kite.API.Application.Services
{
    using CodeName399.Kite.API.Constants;
    using CodeName399.Kite.API.Extensions;
    using CodeName399.Kite.API.Helpers;
    using CodeName399.Kite.API.Models;
    using CodeName399.Kite.API.Models.Indicator;
    using CodeName399.Kite.API.Models.WebSocketOrder;
    using CodeName399.Kite.API.Repository;
    using CodeName399.Kite.API.Services;
    using CodeName399.Kite.API.Services.Brokerage;
    using Microsoft.Extensions.Options;

    public class CommonService
    {
        private readonly ILogger<CommonService> _logger;

        private readonly AngelOneConfiguration _options;

        private readonly IAngelOneService _angel;

        private readonly TradingSettingsProvider _settings;

        private readonly RequestHelper _requestHelper;

        private readonly ITradeOrderFileRepository _tradeOrderFileRepository;

        private readonly OrderStatusCache _orderStatusCache;

        private readonly RawMarketDataArchiveService _rawMarketData;

        private readonly TradeCloseHistoryStore _tradeCloseStore;

        protected readonly IBrokerageCalculatorService _brokerage;

        private static readonly TimeZoneInfo IndianTimeZone =
    TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows()
            ? "India Standard Time"
            : "Asia/Kolkata");

        private readonly SemaphoreSlim _quoteSemaphore = new(2, 2);

        private int chargeCoverAveragePriceChangeCandles = 5;

        public CommonService(
            ILogger<CommonService> logger,
            IOptions<AngelOneConfiguration> options,
        IAngelOneService angel,
        TradingSettingsProvider settings,
        RequestHelper requestHelper,
        ITradeOrderFileRepository tradeOrderFileRepository,
        OrderStatusCache orderStatusCache,
        RawMarketDataArchiveService rawMarketData,
        IBrokerageCalculatorService brokerage,
        TradeCloseHistoryStore tradeCloseStore
        )
        {
            _logger = logger;
            _options = options.Value;
            _angel = angel;
            _settings = settings;
            _requestHelper = requestHelper;
            _tradeOrderFileRepository = tradeOrderFileRepository;
            _orderStatusCache = orderStatusCache;
            _rawMarketData = rawMarketData;
            _brokerage = brokerage;
            _tradeCloseStore = tradeCloseStore;
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
        /// Returns owned positions keyed by normalized trading symbol. Persisted
        /// TradeOrder records are authoritative; websocket fills recover missing positions.
        /// </summary>
        public async Task<Dictionary<string, TradeOrder>> GetOwnedStocksAsync()
        {
            var owned = new Dictionary<string, TradeOrder>(StringComparer.OrdinalIgnoreCase);
            foreach (var trade in await _tradeOrderFileRepository.GetOpenTradesAsync())
            {
                if (!string.IsNullOrWhiteSpace(trade.Symbol))
                    owned[trade.Symbol.Trim().ToUpperInvariant()] = trade;
            }

            var netBySymbol = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var latestBuyBySymbol = new Dictionary<string, OrderStatusMessage>(StringComparer.OrdinalIgnoreCase);
            foreach (var order in _orderStatusCache.GetAll())
            {
                if (string.IsNullOrWhiteSpace(order.Symbol) || order.FilledQuantity <= 0)
                    continue;
                var status = order.Status?.Trim() ?? string.Empty;
                if (!status.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase)
                    && !status.Equals("PARTIALLY_FILLED", StringComparison.OrdinalIgnoreCase)
                    && !status.Equals("PARTIALLY FILLED", StringComparison.OrdinalIgnoreCase))
                    continue;

                var symbol = order.Symbol.Trim().ToUpperInvariant();
                var sign = order.TransactionType.Equals("BUY", StringComparison.OrdinalIgnoreCase) ? 1
                    : order.TransactionType.Equals("SELL", StringComparison.OrdinalIgnoreCase) ? -1 : 0;
                if (sign == 0) continue;

                netBySymbol[symbol] = netBySymbol.GetValueOrDefault(symbol) + sign * order.FilledQuantity;
                if (sign > 0 && (!latestBuyBySymbol.TryGetValue(symbol, out var previous)
                    || order.UpdatedTime >= previous.UpdatedTime))
                    latestBuyBySymbol[symbol] = order;
            }

            foreach (var position in netBySymbol.Where(x => x.Value > 0))
            {
                if (owned.ContainsKey(position.Key) || !latestBuyBySymbol.TryGetValue(position.Key, out var buy))
                    continue;

                owned[position.Key] = new TradeOrder
                {
                    Symbol = position.Key,
                    SymbolToken = buy.SymbolToken,
                    Exchange = string.IsNullOrWhiteSpace(buy.Exchange) ? "NSE" : buy.Exchange,
                    OrderType = string.IsNullOrWhiteSpace(buy.OrderType) ? "MARKET" : buy.OrderType,
                    Quantity = position.Value,
                    EntryPrice = buy.AveragePrice,
                    EntryTime = buy.UpdatedTime,
                    ExecutionMode = "Live"
                };
            }

            return owned;
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

            var exchange = "NSE";

            var quotes = await GetQuotesInChunksAsync(instruments, jwt);

            return quotes;
        }

        private async Task<List<MarketQuoteData>> GetQuotesInChunksAsync(List<Instrument> instruments, string jwt)
        {
            var result = new List<MarketQuoteData>();
            foreach (var group in instruments.GroupBy(x => x.exch_seg))
            {
                foreach (var batch in ChunkTokens(group.Select(x => x.token).ToList()))
                    result.AddRange(await GetBatchAsync(batch, jwt, group.Key));
            }
            return result;
        }

        private IEnumerable<List<string>> ChunkTokens(List<string> tokens, int size = 50)
        {
            for (var i = 0; i < tokens.Count; i += size)
                yield return tokens.Skip(i).Take(size).ToList();
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

        public List<MarketQuoteData> GetTopQuotes(List<MarketQuoteData> quotes, Dictionary<string, TradeOrder> ownedStocks, int count)
        {
            return quotes
                .Where(x => !ownedStocks.ContainsKey(x.TradingSymbol) && !ownedStocks.Values.Any(t => string.Equals(t.SymbolToken, x.SymbolToken, StringComparison.OrdinalIgnoreCase)))
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
                {
                    candlesByToken[result.Token] = result.Candles;

                    // Calculate indicators immediately after each token's candles are downloaded
                    // and write them directly into the corresponding quote.
                    var quote = quotes.FirstOrDefault(
                        q => string.Equals(q.SymbolToken?.Trim(), result.Token, StringComparison.OrdinalIgnoreCase));

                    if (quote != null)
                        UpdateQuoteIndicators(quote, result.Candles, interval);
                }

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

        /// <summary>
        /// Downloads candles for owned trading symbols. Symbols are resolved against the
        /// instrument master, then downloaded in batches using the same limits as quote downloads.
        /// </summary>
        public async Task<Dictionary<string, List<Candle>>> DownloadCandlesInBatchesAsync(
            HashSet<string> ownedQuotes,
            List<Instrument> instruments,
            string jwtToken,
            string interval,
            DateTime fromDate,
            DateTime toDate,
            int batchSize = 2,
            int batchDelay = 1)
        {
            var candlesByToken = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);

            if (ownedQuotes == null || ownedQuotes.Count == 0 || instruments == null || instruments.Count == 0)
                return candlesByToken;

            if (string.IsNullOrWhiteSpace(jwtToken))
                throw new ArgumentException("JWT token is required to download historical candles.", nameof(jwtToken));

            batchSize = Math.Max(1, Math.Min(batchSize, 3));

            var instrumentsBySymbol = instruments
                .Where(x => !string.IsNullOrWhiteSpace(x.symbol) && !string.IsNullOrWhiteSpace(x.token))
                .GroupBy(x => x.symbol.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            var requestedInstruments = ownedQuotes
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol))
                .Select(symbol => symbol.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(symbol => instrumentsBySymbol.TryGetValue(symbol, out var instrument) ? instrument : null)
                .Where(instrument => instrument != null)
                .GroupBy(instrument => instrument!.token.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First()!)
                .ToList();

            var unresolvedSymbols = ownedQuotes
                .Where(symbol => !string.IsNullOrWhiteSpace(symbol) && !instrumentsBySymbol.ContainsKey(symbol.Trim()))
                .ToList();

            foreach (var symbol in unresolvedSymbols)
                _logger.LogWarning("Could not resolve owned symbol {Symbol} in instrument master; candle download skipped.", symbol);

            for (var offset = 0; offset < requestedInstruments.Count; offset += batchSize)
            {
                var batch = requestedInstruments.Skip(offset).Take(batchSize).ToList();
                var tasks = batch.Select(async instrument =>
                {
                    var token = instrument.token.Trim();
                    var exchange = string.IsNullOrWhiteSpace(instrument.exch_seg)
                        ? "NSE"
                        : instrument.exch_seg.Trim().ToUpperInvariant();

                    try
                    {
                        var candles = await DownloadCandleAsync(
                            token, jwtToken, interval, fromDate, toDate, exchange);
                        return (Token: token, Candles: candles ?? new List<Candle>());
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to download {Interval} candles for owned symbol {Symbol} (token {Token})",
                            interval, instrument.symbol, token);
                        return (Token: token, Candles: new List<Candle>());
                    }
                });

                foreach (var result in await Task.WhenAll(tasks))
                    candlesByToken[result.Token] = result.Candles;

                if (offset + batchSize < requestedInstruments.Count && batchDelay > 0)
                    await Task.Delay(TimeSpan.FromSeconds(batchDelay));
            }

            _logger.LogInformation(
                "Downloaded {LoadedCount} of {RequestedCount} owned stocks with {Interval} candles in batches of {BatchSize}",
                candlesByToken.Count(x => x.Value.Count > 0), requestedInstruments.Count, interval, batchSize);

            return candlesByToken;
        }

        private void UpdateQuoteIndicators(MarketQuoteData quote, List<Candle> candles, string interval)
        {
            // EMA21 for the previous candle requires at least 21 candles before the latest one.
            // 22 candles therefore provide current and previous-candle EMA9/EMA21 values.
            if (candles == null || candles.Count < 22)
            {
                _logger.LogDebug(
                    "Skipping indicator update for {Symbol} (token {Token}): {Count} {Interval} candles; 22 required.",
                    quote.TradingSymbol,
                    quote.SymbolToken,
                    candles?.Count ?? 0,
                    interval);
                return;
            }

            var orderedCandles = candles
                .Where(c => c != null && c.Close > 0 && c.High > 0 && c.Low > 0 && c.High >= c.Low)
                .GroupBy(c => c.Timestamp)
                .Select(g => g.Last())
                .OrderBy(c => c.Timestamp)
                .ToList();

            if (orderedCandles.Count < 22)
                return;

            var previousCandles = orderedCandles.Take(orderedCandles.Count - 1).ToList();

            quote.Ema9 = Math.Round(CalculateEma(orderedCandles.Select(c => c.Close).ToList(), 9), 6, MidpointRounding.AwayFromZero);
            quote.Ema21 = Math.Round(CalculateEma(orderedCandles.Select(c => c.Close).ToList(), 21), 6, MidpointRounding.AwayFromZero);
            quote.PreviousCandleEma9 = Math.Round(CalculateEma(previousCandles.Select(c => c.Close).ToList(), 9), 6, MidpointRounding.AwayFromZero);
            quote.PreviousCandleEma21 = Math.Round(CalculateEma(previousCandles.Select(c => c.Close).ToList(), 21), 6, MidpointRounding.AwayFromZero);
            quote.ChoppinessIndex = Math.Round(CalculateChoppinessIndex(orderedCandles, 14), 6, MidpointRounding.AwayFromZero);

            _logger.LogDebug(
                "Updated quote indicators for {Symbol} ({Token}) using {Count} {Interval} candles: EMA9={Ema9}, EMA21={Ema21}, PreviousEMA9={PreviousEma9}, PreviousEMA21={PreviousEma21}, Choppiness={Choppiness}",
                quote.TradingSymbol,
                quote.SymbolToken,
                orderedCandles.Count,
                interval,
                quote.Ema9,
                quote.Ema21,
                quote.PreviousCandleEma9,
                quote.PreviousCandleEma21,
                quote.ChoppinessIndex);
        }

        private static decimal CalculateEma(IReadOnlyList<decimal> closingPrices, int period)
        {
            if (closingPrices == null || closingPrices.Count < period)
                return 0m;

            // Standard EMA: SMA of the first period as the seed, then alpha = 2 / (period + 1).
            decimal ema = closingPrices.Take(period).Average();
            decimal alpha = 2m / (period + 1m);

            for (var i = period; i < closingPrices.Count; i++)
                ema = (closingPrices[i] * alpha) + (ema * (1m - alpha));

            return ema;
        }

        private static decimal CalculateChoppinessIndex(IReadOnlyList<Candle> candles, int period)
        {
            if (candles == null || candles.Count <= period)
                return 0m;

            var window = candles.TakeLast(period).ToList();
            var startIndex = candles.Count - period;
            decimal sumTrueRange = 0m;

            for (var i = startIndex; i < candles.Count; i++)
            {
                var current = candles[i];
                var previousClose = candles[i - 1].Close;
                var trueRange = Math.Max(
                    current.High - current.Low,
                    Math.Max(
                        Math.Abs(current.High - previousClose),
                        Math.Abs(current.Low - previousClose)));

                sumTrueRange += trueRange;
            }

            var highestHigh = window.Max(c => c.High);
            var lowestLow = window.Min(c => c.Low);
            var range = highestHigh - lowestLow;

            if (range <= 0m || sumTrueRange <= 0m)
                return 0m;

            var ratio = (double)(sumTrueRange / range);
            var index = 100d * Math.Log10(ratio) / Math.Log10(period);

            if (double.IsNaN(index) || double.IsInfinity(index))
                return 0m;

            return (decimal)index;
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

        public async Task PlaceBuyOrders(HashSet<MarketQuoteData> quotes, Dictionary<string, List<Candle>> candles, string jwtToken)
        {
            foreach (var batch in quotes.Chunk(_settings.Current.BuyBatchSize))
            {
                var batchTasks = batch.Select(async quote =>
                {
                    if (!ValidateBuyConditions(quote))
                        return;

                    var quoteCandles = candles.TryGetValue(quote.SymbolToken, out var loadedCandles)
                        ? loadedCandles
                        : new List<Candle>();

                    var (quantity, margin, ltp) = await GetQuantitesAndMargin(quote, jwtToken, quoteCandles);

                    if (quantity > 0)
                    {
                        quote.Quantity = quantity;
                        quote.TotalInvestment = margin;
                        quote.Ltp = ltp;

                        var tradeOrder = MarketQuoteData.ToTradeOrder(quote, _settings.Current.EnableAutoTrading);

                        if (_settings.Current.EnableAutoTrading)
                        {
                            // Persist the intended position before the broker side effect.
                            // If the request times out, retaining this record prevents a duplicate buy
                            // while broker/order-websocket reconciliation determines whether it filled.
                            await _tradeOrderFileRepository.SaveAsync(tradeOrder);

                            var orderTag = $"CN399-BUY-{Guid.NewGuid():N}"[..19];
                            PlaceOrderResponse buyResponse;
                            try
                            {
                                buyResponse = await _angel.PlaceOrderAsync(
                                    new PlaceOrderRequest
                                    {
                                        TradingSymbol = tradeOrder.Symbol,
                                        SymbolToken = tradeOrder.SymbolToken,
                                        Exchange = tradeOrder.Exchange,
                                        TransactionType = "BUY",
                                        Quantity = tradeOrder.Quantity,
                                        OrderType = "MARKET",
                                        ProductType = "INTRADAY",
                                        Duration = "DAY",
                                        Variety = "NORMAL",
                                        OrderTag = orderTag
                                    },
                                    forceLive: true);
                            }
                            catch (Exception orderException)
                            {
                                _logger.LogError(orderException,
                                    "LIVE BUY ORDER RESULT UNKNOWN Symbol={Symbol} Quantity={Quantity}; persisted trade retained for reconciliation.",
                                    tradeOrder.Symbol, tradeOrder.Quantity);
                                return;
                            }

                            if (buyResponse?.Status != true)
                            {
                                await _tradeOrderFileRepository.DeleteAsync(tradeOrder.Symbol);
                                _logger.LogError(
                                    "LIVE BUY ORDER REJECTED Symbol={Symbol} Quantity={Quantity} Message={Message}",
                                    tradeOrder.Symbol, tradeOrder.Quantity, buyResponse?.Message);
                                return;
                            }

                            _logger.LogInformation(
                                "LIVE BUY ORDER SUBMITTED Symbol={Symbol} Quantity={Quantity} OrderId={OrderId} UniqueOrderId={UniqueOrderId}",
                                tradeOrder.Symbol, tradeOrder.Quantity,
                                buyResponse.Data?.OrderId, buyResponse.Data?.UniqueOrderId);
                        }
                        else
                        {
                            await _tradeOrderFileRepository.SaveAsync(tradeOrder);
                        }

                        EmailHelper.SendEmail(
                    EmailHelper.BuildBuyOrderEmail(tradeOrder),
                    _settings.Current,
                    $"{tradeOrder.ExecutionMode} EXECUTED {tradeOrder.Symbol}",
                    true);
                    }
                });

                await Task.WhenAll(batchTasks);
            }
        }

        private bool ValidateBuyConditions(MarketQuoteData quote)
        {
            if (quote == null)
                return false;
            if (quote.Ema9 <= 0 || quote.Ema21 <= 0 || quote.PreviousCandleEma9 <= 0 || quote.PreviousCandleEma21 <= 0)
                return false;
            if (quote.ChoppinessIndex > 61.8m)
                return false;
            if (quote.Ltp <= quote.Ema9 || quote.Ltp <= quote.Ema21)
                return false;
            if (quote.Ema9 <= quote.Ema21)
                return false;
            if (quote.PreviousCandleEma9 >= quote.PreviousCandleEma21)
                return false;
            return true;
        }

        private async Task<(int Quantity, decimal Margin, decimal Ltp)> GetQuantitesAndMargin(MarketQuoteData quote, string jwtToken, List<Candle> candles)
        {
            var ltp = await _angel.GetLtpDataAsync(quote.Exchange, quote.TradingSymbol, quote.SymbolToken);
            if (!ltp.HasValue || ltp.Value <= 0m)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=AngelOneLtp Reason=LtpUnavailable", quote.TradingSymbol);
                return (0, 0m, 0m);
            }

            var currentLtp = ltp.Value;
            decimal lowestPriceChange;
            try
            {
                lowestPriceChange = await GetLowestPriceChangeAsync(quote, jwtToken, candles);
                quote.LowestPriceChange = lowestPriceChange;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ORDER BLOCKED Symbol={Symbol} Gate=LowestPriceChange Reason=OneMinuteCandleFetchFailed", quote.TradingSymbol);
                lowestPriceChange = 0m;
            }

            if (lowestPriceChange <= 0m)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=ChargeCoverQuantity Price={Price:F4} LowestPriceChange={LowestPriceChange:F6} Reason=NoUsableCandleChange", quote.TradingSymbol, currentLtp, lowestPriceChange);

                return (0, 0m, 0m);
            }

            var minimumQuantity = await FindMinimumChargeCoverQuantityAsync(quote, currentLtp, lowestPriceChange);
            if (minimumQuantity <= 0)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=ChargeCoverQuantity Price={Price:F4} LowestPriceChange={LowestPriceChange:F6} Reason=NoQuantityFound", quote.TradingSymbol, currentLtp, lowestPriceChange);

                return (0, 0m, 0m);
            }

            var availableAtPrice = GetAvailableAtCurrentPrice(quote);
            _logger.LogInformation("ORDER DIAGNOSTIC ChargeCoverAndDepth Symbol={Symbol} MinimumChargeCoverQuantity={MinimumQuantity} AvailableAskQuantityAtCurrentPrice={AvailableQuantity} CurrentPrice={Price:F4} LowestPriceChange={LowestPriceChange:F6} PriceChangeLookbackCandles={CandleCount}", quote.TradingSymbol, minimumQuantity, availableAtPrice, currentLtp, lowestPriceChange, chargeCoverAveragePriceChangeCandles);
            if (availableAtPrice < minimumQuantity)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=MarketDepth MinimumRequiredQuantity={MinimumQuantity} AvailableAtCurrentPrice={AvailableQuantity} Price={Price:F4}", quote.TradingSymbol, minimumQuantity, availableAtPrice, currentLtp);

                return (0, 0m, 0m);
            }

            var affordable = await GetAffordableQuantity(quote, currentLtp, minimumQuantity, availableAtPrice, await _angel.GetAvailableBalanceAsync());
            var quantity = affordable.Quantity;
            var requiredMargin = affordable.Margin;
            if (quantity < minimumQuantity)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=AffordableQuantity MinimumRequiredQuantity={MinimumQuantity} AvailableAtPrice={AvailableAtPrice} AvailableBalance={Balance:F2} MarginReturned={Margin:F2}", quote.TradingSymbol, minimumQuantity, availableAtPrice, await _angel.GetAvailableBalanceAsync(), requiredMargin);

                return (0, 0m, 0m);
            }

            return (quantity, requiredMargin, currentLtp);
        }

        private async Task<decimal> GetLowestPriceChangeAsync(MarketQuoteData quote, string jwtToken, List<Candle> candles)
        {
            var candleCount = Math.Clamp(chargeCoverAveragePriceChangeCandles, 2, 100);
            var completed = (candles ?? new List<Candle>())
                .Where(c => c.Close > 0m)
                .OrderBy(c => c.Timestamp)
                .ToList();

            // Exclude the currently forming candle. Historical data is timestamped in market time.
            var now = DateTime.Now;
            if (completed.Count > 0)
            {
                var latest = completed[^1];
                if (latest.Timestamp > now || now - latest.Timestamp < TimeSpan.FromSeconds(55))
                    completed.RemoveAt(completed.Count - 1);
            }

            if (completed.Count < candleCount)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=LowestPriceChange Reason=InsufficientCompletedOneMinuteCandles Required={Required} Available={Available}", quote.TradingSymbol, candleCount, completed.Count);
                return 0m;
            }

            var recent = completed.TakeLast(candleCount).ToList();
            var changes = recent.Select(c => Math.Abs(c.Close - c.Open)).Where(x => x > 0).ToList();
            var lowestChange = changes.Min();
            _logger.LogInformation("CHARGE COVER LOWEST PRICE CHANGE Symbol={Symbol} CandleCount={CandleCount} LowestAbsoluteCandleBodyChange={LowestChange:F6} LastClose={LastClose:F4} FirstOpen={FirstOpen:F4}", quote.TradingSymbol, candleCount, lowestChange, recent[^1].Close, recent[0].Open);
            return lowestChange;
        }

        private async Task<int> FindMinimumChargeCoverQuantityAsync(MarketQuoteData quote, decimal price, decimal increment)
        {
            if (increment <= 0m)
                throw new InvalidOperationException("Lowest absolute candle-body price change over configured one-minute candles must be greater than zero.");

            // The broker charge-estimate endpoint is rate-limited. Estimate the
            // fixed and variable components from only two quantities per side,
            // then solve locally instead of making an API request on every search
            // iteration. Add a 15% safety buffer to avoid underestimating charges.
            const int sampleQuantity = 1000;
            var buyOne = await _brokerage.CalculateAsync(quote.TradingSymbol, quote.SymbolToken, price, 1,
                BrokerageTransactionType.Buy, quote.Exchange, "INTRADAY");
            var sellOne = await _brokerage.CalculateAsync(quote.TradingSymbol, quote.SymbolToken, price + increment, 1,
                BrokerageTransactionType.Sell, quote.Exchange, "INTRADAY");
            var buySample = await _brokerage.CalculateAsync(quote.TradingSymbol, quote.SymbolToken, price, sampleQuantity,
                BrokerageTransactionType.Buy, quote.Exchange, "INTRADAY");
            var sellSample = await _brokerage.CalculateAsync(quote.TradingSymbol, quote.SymbolToken, price + increment, sampleQuantity,
                BrokerageTransactionType.Sell, quote.Exchange, "INTRADAY");

            static (decimal Fixed, decimal PerUnit) Fit(decimal one, decimal sample, int sampleQty)
            {
                var perUnit = Math.Max(0m, (sample - one) / (sampleQty - 1));
                var fixedPart = Math.Max(0m, one - perUnit);
                return (fixedPart, perUnit);
            }

            var buyModel = Fit(buyOne.TotalCharges, buySample.TotalCharges, sampleQuantity);
            var sellModel = Fit(sellOne.TotalCharges, sellSample.TotalCharges, sampleQuantity);
            decimal EstimatedRoundTripCharges(int quantity) =>
                (buyModel.Fixed + buyModel.PerUnit * quantity +
                 sellModel.Fixed + sellModel.PerUnit * quantity) * 1.15m;

            // The intended rule is gross price movement must exceed the
            // complete round-trip charges. Do not compare a net difference
            // (which already subtracts buy charges) against charges a second time.
            var low = 1;
            var high = 1;
            while (high < 1_000_000 && increment * high <= EstimatedRoundTripCharges(high))
                high = Math.Min(1_000_000, high * 2);

            if (increment * high <= EstimatedRoundTripCharges(high))
            {
                _logger.LogWarning("CHARGE COVER MODEL Symbol={Symbol} Reason=NoQuantityFound Increment={Increment:F6} MaxQuantity={MaxQuantity} EstimatedCharges={Charges:F2}",
                    quote.TradingSymbol, increment, high, EstimatedRoundTripCharges(high));
                return 0;
            }

            while (low < high)
            {
                var middle = low + (high - low) / 2;
                var estimatedCharges = EstimatedRoundTripCharges(middle);
                if (increment * middle > estimatedCharges)
                    high = middle;
                else
                    low = middle + 1;

                quote.EstimatedCharges = estimatedCharges;
            }

            _logger.LogInformation(
                "CHARGE COVER MODEL Symbol={Symbol} Quantity={Quantity} Increment={Increment:F6} EstimatedRoundTripCharges={Charges:F2} SafetyBufferPercent=15 BuyFixed={BuyFixed:F4} BuyPerUnit={BuyPerUnit:F6} SellFixed={SellFixed:F4} SellPerUnit={SellPerUnit:F6} BrokerEstimateCalls=4",
                quote.TradingSymbol, low, increment, EstimatedRoundTripCharges(low),
                buyModel.Fixed, buyModel.PerUnit, sellModel.Fixed, sellModel.PerUnit);
            return low;
        }

        private int GetAvailableAtCurrentPrice(MarketQuoteData quote)
        {
            // For a market BUY, count valid ask-side depth; asks are often above LTP.
            var depth = quote?.Depth?.Sell;
            if (depth == null || depth.Count == 0)
                return 0; // fail closed when the broker provides no ask-side depth.

            // LTP is the last traded price, not the best ask. For a market BUY,
            // valid asks are commonly ABOVE LTP; excluding those levels made the
            // eligible quantity zero and blocked otherwise valid crossover entries.
            // Count available ask-side quantity here; the submitted market order is
            // still subject to broker execution and actual fill price.
            var quantity = depth.Where(x => x.Price > 0m && x.Quantity > 0)
                .Sum(x => Math.Max(0L, x.Quantity));
            return (int)Math.Min(int.MaxValue, quantity);
        }

        private async Task<(int Quantity, decimal Margin)> GetAffordableQuantity(
            MarketQuoteData quote,
            decimal ltp,
            int minimumQuantity,
            int maximumAvailableQuantity,
            decimal availableBalance)
        {
            if (minimumQuantity <= 0 || maximumAvailableQuantity < minimumQuantity)
                return (0, 0m);

            // Binary-search the largest quantity that the broker's INTRADAY margin
            // calculator says can be funded. Do not silently reduce below charge-cover quantity.
            var low = minimumQuantity;
            var high = maximumAvailableQuantity;
            var bestQuantity = 0;
            var bestMargin = 0m;
            while (low <= high)
            {
                var quantity = low + (high - low) / 2;
                var margin = await _angel.GetRequiredMarginAsync(
                    quote.SymbolToken, quantity, ltp,
                    quote.Exchange, "INTRADAY", "BUY");
                _logger.LogInformation(
                    "INTRADAY MARGIN CHECK Symbol={Symbol} Token={Token} Exchange={Exchange} ProductType=INTRADAY OrderType=MARKET Quantity={Quantity} ReferencePrice={Price:F4} BrokerRequiredMargin={Margin:F2} AvailableFunds={Funds:F2}",
                    quote.TradingSymbol, quote.SymbolToken, quote.Exchange, quantity, ltp, margin, availableBalance);

                if (margin > 0m && margin <= availableBalance)
                {
                    bestQuantity = quantity;
                    bestMargin = margin;
                    low = quantity + 1;
                }
                else
                {
                    high = quantity - 1;
                }
            }

            return (bestQuantity, bestMargin);
        }

        public bool ValidateSell(TradeOrder trade, Candle currentCandle, out string outcome)
        {
            outcome = string.Empty;
            if (trade == null || currentCandle == null || trade.EntryPrice <= 0m
                || currentCandle.Close <= 0m || trade.LowestPriceChange <= 0m)
                return false;

            var difference = currentCandle.Close - trade.EntryPrice;
            if (Math.Abs(difference) <= trade.LowestPriceChange)
                return false;

            outcome = difference > 0m ? "PROFIT" : "LOSS";
            return true;
        }

        /// <summary>Evaluates and processes all owned positions concurrently.</summary>
        public async Task PlaceSellOrder(
            Dictionary<string, TradeOrder> ownedStocks,
            Dictionary<string, List<Candle>> candles,
            bool forceSquareOff = false)
        {
            if (ownedStocks == null || ownedStocks.Count == 0 || candles == null || candles.Count == 0)
                return;

            await Task.WhenAll(ownedStocks.Values
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SymbolToken))
                .Select(x => ProcessSellAsync(x, candles, forceSquareOff)));
        }

        private async Task ProcessSellAsync(
            TradeOrder trade,
            Dictionary<string, List<Candle>> candles,
            bool forceSquareOff)
        {
            try
            {
                if (!candles.TryGetValue(trade.SymbolToken, out var stockCandles)
                    || stockCandles == null || stockCandles.Count == 0)
                    return;

                var candle = stockCandles.OrderBy(x => x.Timestamp).Last();
                var outcome = forceSquareOff ? "SQUARE_OFF" : string.Empty;
                if (!forceSquareOff && !ValidateSell(trade, candle, out outcome))
                    return;

                var exitPrice = candle.Close;

                if (_settings.Current.EnableAutoTrading)
                {
                    var response = await _angel.PlaceOrderAsync(new PlaceOrderRequest
                    {
                        TradingSymbol = trade.Symbol,
                        SymbolToken = trade.SymbolToken,
                        Exchange = trade.Exchange,
                        TransactionType = "SELL",
                        ProductType = "INTRADAY",
                        OrderType = "MARKET",
                        Variety = "NORMAL",
                        Duration = "DAY",
                        Quantity = trade.Quantity,
                        IsExitOrder = true,
                        OrderTag = $"CN399-EXIT-{Guid.NewGuid():N}"[..19]
                    });

                    if (response?.Status != true || response.Data == null
                        || string.IsNullOrWhiteSpace(response.Data.OrderId))
                    {
                        _logger.LogWarning("SELL ORDER REJECTED Symbol={Symbol} Message={Message}", trade.Symbol, response?.Message);
                        return;
                    }

                    await _tradeOrderFileRepository.DeleteAsync(trade.Symbol);
                    _logger.LogInformation("SELL ORDER SUBMITTED Symbol={Symbol} Outcome={Outcome} OrderId={OrderId}", trade.Symbol, outcome, response.Data.OrderId);
                    return;
                }

                trade.ExitPrice = exitPrice;
                trade.ExitTime = candle.Timestamp;
                trade.GrossProfit = (exitPrice - trade.EntryPrice) * trade.Quantity;
                trade.NetProfit = trade.GrossProfit - trade.EstimatedCharges;

                await _tradeCloseStore.AppendAsync(trade);
                await _tradeOrderFileRepository.DeleteAsync(
                    string.IsNullOrWhiteSpace(trade.Id) ? trade.Symbol : trade.Id);

                EmailHelper.SendEmail(
                    EmailHelper.BuildSellOrderEmail(trade),
                    _settings.Current,
                    $"{trade.ExecutionMode} CLOSED {trade.Symbol}",
                    true);

                _logger.LogInformation(
                    "PAPER SELL COMPLETED Symbol={Symbol} Outcome={Outcome} Entry={EntryPrice:F4} Exit={ExitPrice:F4} GrossPnL={GrossPnL:F2} NetPnL={NetPnL:F2}",
                    trade.Symbol, outcome, trade.EntryPrice, exitPrice,
                    trade.GrossProfit, trade.NetProfit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process sell for {Symbol}", trade.Symbol);
            }
        }
    }
}
