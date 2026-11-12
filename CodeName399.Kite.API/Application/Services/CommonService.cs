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
    using CodeName399.Kite.API.WebSocket.Interface;
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

        private readonly IAngelWebSocketClient _webSocket;

        private readonly MarketQuotePublisher _quotePublisher;

        private readonly CandleFileStoreService _candleStore;

        protected readonly IBrokerageCalculatorService _brokerage;

        private static readonly TimeZoneInfo IndianTimeZone =
    TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows()
            ? "India Standard Time"
            : "Asia/Kolkata");

        private readonly SemaphoreSlim _quoteSemaphore = new(2, 2);
        private readonly SemaphoreSlim _buyExecutionSemaphore = new(1, 1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _lastVwapExitEvaluation =
            new(StringComparer.OrdinalIgnoreCase);

        // Historical candle API calls are globally serialized across CommonService instances.
        // Angel One may return HTTP 403 when its historical-data access rate is exceeded.
        private static readonly SemaphoreSlim _historicalRequestGate = new(1, 1);
        private static DateTime _nextHistoricalRequestUtc = DateTime.MinValue;
        private static DateTime _historicalCooldownUntilUtc = DateTime.MinValue;
        private static readonly TimeSpan HistoricalRequestSpacing = TimeSpan.FromSeconds(1.2);
        private static readonly TimeSpan HistoricalRateLimitCooldown = TimeSpan.FromMinutes(1);


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
        TradeCloseHistoryStore tradeCloseStore,
        IAngelWebSocketClient webSocket,
        MarketQuotePublisher quotePublisher,
        CandleFileStoreService candleStore
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
            _webSocket = webSocket;
            _quotePublisher = quotePublisher;
            _candleStore = candleStore;
        }

        public DateTime GetIndianTime()
        {
            return TimeZoneInfo.ConvertTimeFromUtc(
                DateTime.UtcNow,
                IndianTimeZone);
        }

        public DateTime GetNextCandleCloseTime()
        {
            var indianTime = GetIndianTime();
            return new DateTime(
                indianTime.Year,
                indianTime.Month,
                indianTime.Day,
                indianTime.Hour,
                indianTime.Minute,
                0,
                DateTimeKind.Unspecified).AddMinutes(1);
        }

        public TimeSpan GetDelayUntilCandleClose(DateTime candleCloseTime)
        {
            var delay = candleCloseTime - GetIndianTime();
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }

        public TimeSpan GetDelayUntilNextCandleClose() =>
            GetDelayUntilCandleClose(GetNextCandleCloseTime());

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

            var quotes = await GetQuotesInChunksAsync(instruments, jwt);
            if (excluded.Count > 0)
                quotes = quotes.Where(q => !excluded.Contains((q.TradingSymbol ?? string.Empty).ToUpperInvariant())).ToList();

            return quotes;
        }

        private async Task<List<MarketQuoteData>> GetQuotesInChunksAsync(List<Instrument> instruments, string jwt)
        {
            // Quote requests were previously issued one 50-token chunk at a time.
            // With a full NSE equity master this consumed most of the minute and
            // caused the next buy evaluation to miss a candle close. Keep a strict
            // concurrency limit of two (also enforced by _quoteSemaphore), while
            // allowing independent exchange/token chunks to overlap.
            var requests = instruments
                .Where(x => !string.IsNullOrWhiteSpace(x.token) && !string.IsNullOrWhiteSpace(x.exch_seg))
                .GroupBy(x => x.exch_seg.Trim(), StringComparer.OrdinalIgnoreCase)
                .SelectMany(group => ChunkTokens(group.Select(x => x.token.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                    .Select(batch => (Exchange: group.Key, Tokens: batch)))
                .ToList();

            var tasks = requests.Select(async request =>
                await GetBatchAsync(request.Tokens, jwt, request.Exchange));
            var batches = await Task.WhenAll(tasks);
            return batches.SelectMany(x => x).ToList();
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
                .Where(x => x.Ltp >= Math.Max(0m, _settings.Current.MinimumStockPrice) && x.Close > 0m && x.Ltp > x.Close)
            .Where(x => GetActivityVolume(x) > 0L)
            .OrderByDescending(GetActivityVolume)
            .ThenByDescending(x => (x.Ltp - x.Close) / x.Close)
            .ThenByDescending(x => x.Open > 0m ? (x.Ltp - x.Open) / x.Open : decimal.MinValue)
            .ThenBy(x => x.Ltp)
            .Take(Math.Max(0, count))
            .ToList();
        }

        /// <summary>
        /// Reads completed tick-built candles directly from local JSON files
        /// and ensures WebSocket subscriptions are active. No historical API or database
        /// request is made by this method.
        /// </summary>
        public async Task<Dictionary<string, List<Candle>>> GetCandlesFromFilesForQuotesAsync(
            List<MarketQuoteData> quotes,
            List<Instrument> instruments,
            string interval)
        {
            var result = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
            if (quotes == null || quotes.Count == 0 || instruments == null || instruments.Count == 0)
                return result;

            var byToken = instruments
                .Where(x => !string.IsNullOrWhiteSpace(x.token))
                .GroupBy(x => x.token.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var quote in quotes.Where(q => !string.IsNullOrWhiteSpace(q.SymbolToken)))
            {
                var token = quote.SymbolToken.Trim();
                if (!byToken.TryGetValue(token, out var instrument))
                    continue;

                var exchange = string.IsNullOrWhiteSpace(instrument.exch_seg)
                    ? "NSE"
                    : instrument.exch_seg.Trim().ToUpperInvariant();

                var fileCandles = await _candleStore.GetAsync(token, instrument.symbol, interval);
                result[token] = fileCandles;
                UpdateQuoteIndicators(quote, fileCandles, interval);

                // Subscribe even when the file has no candles yet; subsequent live ticks
                // will build and persist completed candles for the next evaluation.
                await _webSocket.SubscribeAsync([token], exchange);

                _logger.LogDebug(
                    "TICK CANDLE FILE READ Symbol={Symbol} Token={Token} FileCandles={Count} Interval={Interval}",
                    instrument.symbol, token, fileCandles.Count, interval);
            }

            return result;
        }

        /// <summary>
        /// Reads tick-built candles for currently owned positions directly from JSON files and
        /// keeps their WebSocket subscriptions active. No historical API or database
        /// request is made.
        /// </summary>
        public async Task<Dictionary<string, List<Candle>>> GetCandlesFromFilesForOwnedStocksAsync(
            Dictionary<string, TradeOrder> ownedStocks,
            string interval)
        {
            var result = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
            if (ownedStocks == null || ownedStocks.Count == 0)
                return result;

            foreach (var trade in ownedStocks.Values.Where(x => !string.IsNullOrWhiteSpace(x.SymbolToken)))
            {
                var token = trade.SymbolToken.Trim();
                var fileCandles = await _candleStore.GetAsync(token, trade.Symbol, interval);
                result[token] = fileCandles;
                await _webSocket.SubscribeAsync([token], string.IsNullOrWhiteSpace(trade.Exchange) ? "NSE" : trade.Exchange);
                _logger.LogDebug(
                    "OWNED TICK CANDLE FILE READ Symbol={Symbol} Token={Token} FileCandles={Count} Interval={Interval}",
                    trade.Symbol, token, fileCandles.Count, interval);
            }

            return result;
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
            if (quotes == null || quotes.Count == 0 || instruments == null || instruments.Count == 0) return candlesByToken;
            if (string.IsNullOrWhiteSpace(jwtToken)) throw new ArgumentException("JWT token is required to download historical candles.", nameof(jwtToken));

            var byToken = instruments.Where(x => !string.IsNullOrWhiteSpace(x.token))
                .GroupBy(x => x.token.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var duration = CandleFileStoreService.IntervalDuration(interval);
            var expectedLast = new DateTime(toDate.Year, toDate.Month, toDate.Day, toDate.Hour, toDate.Minute / (int)duration.TotalMinutes * (int)duration.TotalMinutes, 0).Subtract(duration);

            foreach (var quote in quotes.Where(q => !string.IsNullOrWhiteSpace(q.SymbolToken)))
            {
                var token = quote.SymbolToken.Trim();
                if (!byToken.TryGetValue(token, out var instrument)) continue;
                var exchange = string.IsNullOrWhiteSpace(instrument.exch_seg) ? "NSE" : instrument.exch_seg.Trim().ToUpperInvariant();
                var cached = await _candleStore.GetAsync(token, instrument.symbol, interval);
                var fetchFrom = fromDate;
                if (cached.Count > 0)
                {
                    var last = cached.Max(x => x.Timestamp);
                    if (last >= expectedLast)
                    {
                        candlesByToken[token] = cached;
                        UpdateQuoteIndicators(quote, cached, interval);
                        _logger.LogDebug("CANDLE CACHE HIT Symbol={Symbol} Token={Token} Count={Count} Last={Last}", instrument.symbol, token, cached.Count, last);
                        await _webSocket.SubscribeAsync([token], exchange);
                        continue;
                    }
                    fetchFrom = last.Add(duration) > fromDate ? last.Add(duration) : fromDate;
                }

                var rateLimited = false;
                try
                {
                    var downloaded = await DownloadCandleWithRateLimitAsync(token, jwtToken, interval, fetchFrom, toDate, exchange);
                    if (downloaded.Count > 0)
                        await _candleStore.SaveAsync(token, instrument.symbol, interval, downloaded);
                    var combined = await _candleStore.GetAsync(token, instrument.symbol, interval);
                    candlesByToken[token] = combined;
                    UpdateQuoteIndicators(quote, combined, interval);
                    _logger.LogInformation("CANDLE CACHE UPDATED Symbol={Symbol} Token={Token} Cached={Cached} Downloaded={Downloaded} Total={Total}", instrument.symbol, token, cached.Count, downloaded.Count, combined.Count);
                }
                catch (Exception ex)
                {
                    rateLimited = IsHistoricalRateLimitException(ex);
                    _logger.LogWarning(ex, "Candle cache/API load failed for {Symbol} token {Token}. RateLimited={RateLimited}", instrument.symbol, token, rateLimited);
                    candlesByToken[token] = cached;
                    UpdateQuoteIndicators(quote, cached, interval);
                }

                // Keep live ticks flowing even when historical API access is temporarily blocked.
                await _webSocket.SubscribeAsync([token], exchange);
                if (rateLimited)
                {
                    _logger.LogWarning("Stopping remaining historical candle downloads because Angel One rate-limited this process. Cached candles will be used and live ticks will continue. Cooldown={CooldownSeconds}s", HistoricalRateLimitCooldown.TotalSeconds);
                    break;
                }
                if (batchDelay > 0) await Task.Delay(TimeSpan.FromSeconds(batchDelay));
            }

            _logger.LogInformation("Loaded candles for {LoadedCount}/{RequestedCount} stocks from JSON cache/API. Interval={Interval}", candlesByToken.Count(x => x.Value.Count > 0), quotes.Count, interval);
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

            var duration = CandleFileStoreService.IntervalDuration(interval);
            var expectedLast = new DateTime(toDate.Year, toDate.Month, toDate.Day, toDate.Hour, toDate.Minute / (int)duration.TotalMinutes * (int)duration.TotalMinutes, 0).Subtract(duration);

            foreach (var instrument in requestedInstruments)
            {
                var token = instrument.token.Trim();
                var exchange = string.IsNullOrWhiteSpace(instrument.exch_seg) ? "NSE" : instrument.exch_seg.Trim().ToUpperInvariant();
                var cached = await _candleStore.GetAsync(token, instrument.symbol, interval);
                var fetchFrom = fromDate;
                if (cached.Count > 0)
                {
                    var last = cached.Max(x => x.Timestamp);
                    if (last >= expectedLast)
                    {
                        candlesByToken[token] = cached;
                        await _webSocket.SubscribeAsync([token], exchange);
                        continue;
                    }
                    fetchFrom = last.Add(duration) > fromDate ? last.Add(duration) : fromDate;
                }
                var rateLimited = false;
                try
                {
                    var downloaded = await DownloadCandleWithRateLimitAsync(token, jwtToken, interval, fetchFrom, toDate, exchange);
                    if (downloaded.Count > 0) await _candleStore.SaveAsync(token, instrument.symbol, interval, downloaded);
                    candlesByToken[token] = await _candleStore.GetAsync(token, instrument.symbol, interval);
                }
                catch (Exception ex)
                {
                    rateLimited = IsHistoricalRateLimitException(ex);
                    _logger.LogWarning(ex, "Failed to load cached/API candles for owned symbol {Symbol} token {Token}. RateLimited={RateLimited}", instrument.symbol, token, rateLimited);
                    candlesByToken[token] = cached;
                }
                await _webSocket.SubscribeAsync([token], exchange);
                if (rateLimited)
                {
                    _logger.LogWarning("Stopping remaining owned-stock historical downloads because Angel One rate-limited this process. Cached candles will be used and live ticks will continue.");
                    break;
                }
                if (batchDelay > 0) await Task.Delay(TimeSpan.FromSeconds(batchDelay));
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

        private async Task<List<Candle>> DownloadCandleWithRateLimitAsync(
            string token,
            string jwtToken,
            string interval,
            DateTime fromDate,
            DateTime toDate,
            string exchange)
        {
            await _historicalRequestGate.WaitAsync();
            try
            {
                var now = DateTime.UtcNow;
                var allowedAt = _nextHistoricalRequestUtc > _historicalCooldownUntilUtc
                    ? _nextHistoricalRequestUtc
                    : _historicalCooldownUntilUtc;
                var delay = allowedAt - now;
                if (delay > TimeSpan.Zero)
                {
                    _logger.LogDebug("Throttling historical candle request. Token={Token}, DelayMs={DelayMs}", token, delay.TotalMilliseconds);
                    await Task.Delay(delay);
                }

                try
                {
                    return await DownloadCandleAsync(token, jwtToken, interval, fromDate, toDate, exchange);
                }
                catch (AngelApiException ex) when (IsHistoricalRateLimitException(ex))
                {
                    _historicalCooldownUntilUtc = DateTime.UtcNow.Add(HistoricalRateLimitCooldown);
                    _logger.LogError(
                        "Angel One historical API rate limit detected. Token={Token}, Interval={Interval}. No more historical requests will be sent for {CooldownSeconds}s. Response={Response}",
                        token, interval, HistoricalRateLimitCooldown.TotalSeconds, ex.Response);
                    throw;
                }
                finally
                {
                    _nextHistoricalRequestUtc = DateTime.UtcNow.Add(HistoricalRequestSpacing);
                }
            }
            finally
            {
                _historicalRequestGate.Release();
            }
        }

        private static bool IsHistoricalRateLimitException(Exception exception)
        {
            return exception is AngelApiException apiException
                && apiException.StatusCode == System.Net.HttpStatusCode.Forbidden
                && apiException.Response.Contains("exceeding access rate", StringComparison.OrdinalIgnoreCase);
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
                    await _buyExecutionSemaphore.WaitAsync();
                    try
                    {
                        var openTrades = await GetOwnedStocksAsync();
                        var maxTrades = Math.Max(1, _settings.Current.MaxTrades);
                        if (openTrades.Count >= maxTrades)
                        {
                            _logger.LogInformation("BUY SKIPPED Symbol={Symbol} Reason=MaxTradesReached OpenTrades={OpenTrades} MaxTrades={MaxTrades}", quote.TradingSymbol, openTrades.Count, maxTrades);
                            return;
                        }
                        var quoteCandles = candles.TryGetValue(quote.SymbolToken, out var loadedCandles)
                            ? loadedCandles
                            : new List<Candle>();

                        // Entry requires a rising EMA25 trend, a recent pullback towards EMA25/VWAP,
                        // and a bullish recovery candle closing above the previous candle high.
                        var entrySatisfied = EntryStrategyService.TryGetEntryReason(
                            quote, quoteCandles, out var entryReason, out var entryDiagnostics);

                        _logger.LogInformation(
                            "ENTRY STRATEGY EVALUATION Symbol={Symbol} Token={Token} ValidCandlesLoaded={ValidCandlesLoaded} LastClose={LastClose} EMA25={EMA25} VWAP={VWAP} PriceAboveEMA25={PriceAboveEMA25} PriceAboveVWAP={PriceAboveVWAP} Ema25Rising={Ema25Rising} PullbackDetected={PullbackDetected} RecoveryConfirmed={RecoveryConfirmed} PullbackSwingLow={PullbackSwingLow} SuggestedStopLoss={SuggestedStopLoss} EntryConditionSatisfied={EntryConditionSatisfied} EntryRejectionReason={EntryRejectionReason}",
                            quote.TradingSymbol,
                            quote.SymbolToken,
                            entryDiagnostics.ValidCandlesLoaded,
                            entryDiagnostics.LastClose,
                            entryDiagnostics.Ema25,
                            entryDiagnostics.Vwap,
                            entryDiagnostics.PriceAboveEma25,
                            entryDiagnostics.PriceAboveVwap,
                            entryDiagnostics.Ema25Rising,
                            entryDiagnostics.PullbackDetected,
                            entryDiagnostics.RecoveryConfirmed,
                            entryDiagnostics.PullbackSwingLow,
                            entryDiagnostics.SuggestedStopLoss,
                            entryDiagnostics.EntryConditionSatisfied,
                            entryDiagnostics.EntryRejectionReason);

                        if (!entrySatisfied)
                        {
                            _logger.LogDebug(
                                "BUY SKIPPED after EMA25 + VWAP evaluation. Symbol={Symbol} Token={Token} Reason={Reason}",
                                quote.TradingSymbol, quote.SymbolToken, entryDiagnostics.EntryRejectionReason);
                            return;
                        }

                        var (quantity, margin, ltp) = await GetQuantitesAndMargin(quote, quoteCandles);

                        if (quantity > 0)
                        {
                            quote.Quantity = quantity;
                            quote.TotalInvestment = margin;
                            quote.Ltp = ltp;

                            var tradeOrder = MarketQuoteData.ToTradeOrder(quote, _settings.Current.EnableAutoTrading);
                            tradeOrder.EntryReason = entryReason;
                            tradeOrder.StopLoss = entryDiagnostics.SuggestedStopLoss.HasValue &&
                                                  entryDiagnostics.SuggestedStopLoss.Value > 0m &&
                                                  entryDiagnostics.SuggestedStopLoss.Value < tradeOrder.EntryPrice
                                ? entryDiagnostics.SuggestedStopLoss.Value
                                : CalculateInitialStopLoss(
                                    tradeOrder.EntryPrice,
                                    tradeOrder.LowestPriceChange,
                                    _settings.Current.StopLossPercent);
                            _logger.LogInformation(
                                "INITIAL PULLBACK STOP LOSS SET Symbol={Symbol} EntryPrice={EntryPrice:F4} PullbackSwingLow={PullbackSwingLow:F4} StopLoss={StopLoss:F4}",
                                tradeOrder.Symbol, tradeOrder.EntryPrice,
                                entryDiagnostics.PullbackSwingLow, tradeOrder.StopLoss);
                            tradeOrder.TargetPrice = tradeOrder.EntryPrice +
                                (tradeOrder.EstimatedCharges / Math.Max(1, tradeOrder.Quantity)) +
                                (Math.Max(0m, _settings.Current.TargetLowestPriceChangeMultiplier) * tradeOrder.LowestPriceChange);

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

                            await _quotePublisher.SetOwnedAsync(tradeOrder.SymbolToken, tradeOrder.Quantity, tradeOrder.TotalInvestment, tradeOrder.StopLoss);
                            // Ensure the newly opened position receives live ticks for immediate exit monitoring.
                            await _webSocket.SubscribeAsync([tradeOrder.SymbolToken], tradeOrder.Exchange);

                            EmailHelper.SendEmail(
                        EmailHelper.BuildBuyOrderEmail(tradeOrder),
                        _settings.Current,
                        $"{tradeOrder.ExecutionMode} EXECUTED {tradeOrder.Symbol}",
                        true);
                        }
                    }
                    finally
                    {
                        _buyExecutionSemaphore.Release();
                    }
                });

                await Task.WhenAll(batchTasks);
            }
        }

        private async Task<(int Quantity, decimal Margin, decimal Ltp)> GetQuantitesAndMargin(
            MarketQuoteData quote,
            List<Candle> candles)
        {
            // LTP is retained as a reference, but a MARKET BUY is estimated from
            // the ask-side order book. The weighted average ask price is used for
            // margin, charges and expected P&L rather than assuming every share fills at LTP.
            var marginalPrice = await _angel.GetLtpDataAsync(
                quote.Exchange, quote.TradingSymbol, quote.SymbolToken);

            if (!marginalPrice.HasValue || marginalPrice.Value <= 0m)
            {
                _logger.LogWarning(
                    "ORDER BLOCKED Symbol={Symbol} Gate=MarginalPrice Reason=PriceUnavailable",
                    quote.TradingSymbol);
                return (0, 0m, 0m);
            }

            var referencePrice = marginalPrice.Value;
            var lowestPriceChange = GetLowestPriceChange(quote, candles);
            quote.LowestPriceChange = lowestPriceChange;

            // A negative or zero worst candle body is not a positive expected move.
            // Rejecting it is intentional: do not turn a falling candle into a gain.
            if (lowestPriceChange <= 0m)
            {
                _logger.LogWarning(
                    "ORDER BLOCKED Symbol={Symbol} Gate=LowestPriceChange Reason=LowestSignedCandleChangeNotPositive LowestChange={LowestChange:F6}",
                    quote.TradingSymbol, lowestPriceChange);
                return (0, 0m, 0m);
            }

            var askLevels = GetValidAskLevels(quote);
            var maximumMarketQuantity = (int)Math.Min(int.MaxValue,
                askLevels.Sum(x => x.Quantity));
            if (maximumMarketQuantity <= 0)
            {
                _logger.LogWarning(
                    "ORDER BLOCKED Symbol={Symbol} Gate=MarketDepth Reason=NoAvailableAskQuantity ReferencePrice={Price:F4}",
                    quote.TradingSymbol, referencePrice);
                return (0, 0m, 0m);
            }

            var availableBalance = await _angel.GetAvailableBalanceAsync();
            if (availableBalance <= 0m)
            {
                _logger.LogWarning(
                    "ORDER BLOCKED Symbol={Symbol} Gate=AvailableBalance Reason=NoFunds AvailableBalance={Balance:F2}",
                    quote.TradingSymbol, availableBalance);
                return (0, 0m, 0m);
            }

            // Find the greatest quantity whose broker-calculated intraday margin
            // fits the available balance. Searching only up to market depth is
            // equivalent for the final min(affordable, market-available) rule.
            var affordable = await FindMaximumAffordableQuantityAsync(
                quote, askLevels, maximumMarketQuantity, availableBalance);

            var quantity = Math.Min(affordable.Quantity, maximumMarketQuantity);
            if (quantity <= 0)
            {
                _logger.LogWarning(
                    "ORDER BLOCKED Symbol={Symbol} Gate=AffordableQuantity Reason=NoQuantityWithinAvailableBalance MarketAvailable={MarketAvailable} AvailableBalance={Balance:F2}",
                    quote.TradingSymbol, maximumMarketQuantity, availableBalance);
                return (0, 0m, 0m);
            }

            // Reuse the margin returned for the selected maximum affordable
            // quantity. The helper's search is bounded by market depth, so the
            // selected quantity is the same quantity for which this margin was checked.
            var margin = affordable.Margin;
            var executionPrice = GetAskSideWeightedAveragePrice(askLevels, quantity);
            if (executionPrice <= 0m || margin <= 0m)
            {
                _logger.LogWarning(
                    "ORDER BLOCKED Symbol={Symbol} Gate=ExecutionEstimate Reason=InvalidDepthWeightedPriceOrMargin Quantity={Quantity} Price={ExecutionPrice:F4} Margin={Margin:F2}",
                    quote.TradingSymbol, quantity, executionPrice, margin);
                return (0, 0m, 0m);
            }

            var buyCharges = await _brokerage.CalculateAsync(
                quote.TradingSymbol, quote.SymbolToken, executionPrice, quantity,
                BrokerageTransactionType.Buy, quote.Exchange, "INTRADAY");

            var estimatedSellPrice = executionPrice + lowestPriceChange;
            var sellCharges = await _brokerage.CalculateAsync(
                quote.TradingSymbol, quote.SymbolToken, estimatedSellPrice, quantity,
                BrokerageTransactionType.Sell, quote.Exchange, "INTRADAY");

            var buyValue = quantity * executionPrice;
            var estimatedSellValue = quantity * estimatedSellPrice;
            var totalCharges = buyCharges.TotalCharges + sellCharges.TotalCharges;
            var requiredSellValue = buyValue + totalCharges;

            quote.EstimatedCharges = totalCharges;
            quote.Quantity = quantity;
            quote.TotalInvestment = margin;
            quote.Ltp = executionPrice;

            _logger.LogInformation(
                "QUANTITY PROFITABILITY CHECK Symbol={Symbol} Quantity={Quantity} ReferenceLtp={ReferenceLtp:F4} EstimatedBuyVwap={EstimatedBuyVwap:F4} LowestSignedPriceChange={LowestPriceChange:F6} MaximumAffordableQuantity={MaximumAffordableQuantity} MaximumMarketQuantity={MaximumMarketQuantity} AvailableBalance={AvailableBalance:F2} Margin={Margin:F2} BuyValue={BuyValue:F2} EstimatedSellPrice={EstimatedSellPrice:F4} EstimatedSellValue={EstimatedSellValue:F2} BuyCharges={BuyCharges:F2} SellCharges={SellCharges:F2} TotalCharges={TotalCharges:F2} RequiredSellValue={RequiredSellValue:F2}",
                quote.TradingSymbol, quantity, referencePrice, executionPrice, lowestPriceChange,
                affordable.Quantity, maximumMarketQuantity, availableBalance,
                margin, buyValue, estimatedSellPrice, estimatedSellValue,
                buyCharges.TotalCharges, sellCharges.TotalCharges,
                totalCharges, requiredSellValue);

            if (estimatedSellValue <= requiredSellValue)
            {
                _logger.LogWarning(
                    "ORDER BLOCKED Symbol={Symbol} Gate=RoundTripProfitability Quantity={Quantity} EstimatedSellValue={SellValue:F2} RequiredSellValue={RequiredValue:F2} TotalCharges={Charges:F2} Reason=ExpectedMoveDoesNotCoverBuyAndSellCharges",
                    quote.TradingSymbol, quantity, estimatedSellValue,
                    requiredSellValue, totalCharges);
                quote.Quantity = 0;
                quote.TotalInvestment = 0m;
                return (0, 0m, 0m);
            }

            _logger.LogInformation(
                "QUANTITY ACCEPTED Symbol={Symbol} Quantity={Quantity} Margin={Margin:F2} BuyCharges={BuyCharges:F2} SellCharges={SellCharges:F2} TotalCharges={TotalCharges:F2}",
                quote.TradingSymbol, quantity, margin,
                buyCharges.TotalCharges, sellCharges.TotalCharges, totalCharges);

            return (quantity, margin, executionPrice);
        }

        private async Task<(int Quantity, decimal Margin)> FindMaximumAffordableQuantityAsync(
            MarketQuoteData quote,
            IReadOnlyList<MarketQuoteDepthLevel> askLevels,
            int maximumMarketQuantity,
            decimal availableBalance)
        {
            if (maximumMarketQuantity <= 0 || askLevels.Count == 0 || availableBalance <= 0m)
                return (0, 0m);

            var low = 1;
            var high = maximumMarketQuantity;
            var bestQuantity = 0;
            var bestMargin = 0m;

            // Margin generally increases with quantity. Binary search finds the
            // largest quantity the broker says is affordable without exceeding
            // the actual displayed market depth.
            while (low <= high)
            {
                var quantity = low + (high - low) / 2;
                var estimatedBuyVwap = GetAskSideWeightedAveragePrice(askLevels, quantity);
                if (estimatedBuyVwap <= 0m)
                {
                    high = quantity - 1;
                    continue;
                }

                decimal requiredMargin;
                try
                {
                    requiredMargin = await _angel.GetRequiredMarginAsync(
                        quote.SymbolToken, quantity, estimatedBuyVwap,
                        quote.Exchange, "INTRADAY", "BUY");
                }
                catch (Exception ex)
                {
                    // An API/rate-limit failure is not evidence that the order is unaffordable.
                    // Abort this symbol's sizing attempt instead of continuing to hammer the API.
                    _logger.LogError(ex,
                        "ORDER BLOCKED Symbol={Symbol} Gate=MarginApi Reason=MarginCalculationFailed Quantity={Quantity}; no buy order will be submitted.",
                        quote.TradingSymbol, quantity);
                    return (0, 0m);
                }

                _logger.LogInformation(
                    "INTRADAY MARGIN CHECK Symbol={Symbol} Token={Token} Quantity={Quantity} EstimatedBuyVwap={EstimatedBuyVwap:F4} RequiredMargin={RequiredMargin:F2} AvailableBalance={AvailableBalance:F2}",
                    quote.TradingSymbol, quote.SymbolToken, quantity,
                    estimatedBuyVwap, requiredMargin, availableBalance);

                if (requiredMargin > 0m && requiredMargin <= availableBalance)
                {
                    bestQuantity = quantity;
                    bestMargin = requiredMargin;
                    low = quantity + 1;
                }
                else
                {
                    high = quantity - 1;
                }
            }

            return (bestQuantity, bestMargin);
        }

        private decimal GetLowestPriceChange(MarketQuoteData quote, List<Candle> candles)
        {
            var candleCount = Math.Clamp(_settings.Current.ChargeCoverLowestPriceChangeCandles, 2, 100);
            var completed = (candles ?? new List<Candle>())
                .Where(c => c.Close > 0m)
                .OrderBy(c => c.Timestamp)
                .ToList();

            // Exclude the currently forming candle. Historical data is timestamped in market time.
            var now = GetIndianTime();
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
            // Consider only bullish candle bodies. Bearish and flat candles are
            // ignored; use the smallest positive body change as the conservative
            // expected move for the charge-cover calculation.
            var positiveChanges = recent
                .Select(c => c.Close - c.Open)
                .Where(change => change > 0m)
                .ToList();

            if (positiveChanges.Count == 0)
            {
                _logger.LogWarning(
                    "ORDER BLOCKED Symbol={Symbol} Gate=LowestPriceChange Reason=NoPositiveCandleBodyChanges CandleCount={CandleCount} PositiveChangeCount=0",
                    quote.TradingSymbol, candleCount);
                return 0m;
            }

            var lowestPositiveChange = positiveChanges.Min();
            _logger.LogInformation(
                "CHARGE COVER LOWEST POSITIVE PRICE CHANGE Symbol={Symbol} CandleCount={CandleCount} PositiveChangeCount={PositiveChangeCount} LowestPositiveChange={LowestPositiveChange:F6} LastClose={LastClose:F4} FirstOpen={FirstOpen:F4}",
                quote.TradingSymbol, candleCount, positiveChanges.Count, lowestPositiveChange, recent[^1].Close, recent[0].Open);
            return lowestPositiveChange;
        }

        private static List<MarketQuoteDepthLevel> GetValidAskLevels(MarketQuoteData quote)
        {
            return (quote?.Depth?.Sell ?? [])
                .Where(x => x.Price > 0m && x.Quantity > 0)
                .OrderBy(x => x.Price)
                .ToList();
        }

        /// <summary>
        /// Estimates the volume-weighted average price for buying the requested
        /// quantity by walking the visible ask book from the best ask upward.
        /// Returns zero if displayed depth cannot fill the complete quantity.
        /// </summary>
        private static decimal GetAskSideWeightedAveragePrice(
            IReadOnlyList<MarketQuoteDepthLevel> askLevels,
            int requestedQuantity)
        {
            if (requestedQuantity <= 0 || askLevels == null || askLevels.Count == 0)
                return 0m;

            long remaining = requestedQuantity;
            decimal totalValue = 0m;
            long filled = 0;

            foreach (var level in askLevels.OrderBy(x => x.Price))
            {
                if (level.Price <= 0m || level.Quantity <= 0)
                    continue;

                var take = Math.Min(remaining, level.Quantity);
                totalValue += take * level.Price;
                filled += take;
                remaining -= take;

                if (remaining == 0)
                    break;
            }

            return remaining == 0 && filled > 0
                ? totalValue / filled
                : 0m;
        }

        private static decimal CalculateInitialStopLoss(
            decimal entryPrice,
            decimal lowestPriceChange,
            decimal fallbackStopLossPercent)
        {
            if (entryPrice <= 0m)
                return 0m;

            // Use the requested price-action stop: EntryPrice - (LowestPriceChange * 2).
            // Retain the percentage fallback only for legacy positions without a valid
            // LowestPriceChange value, so they do not receive an invalid stop at entry.
            if (lowestPriceChange > 0m)
            {
                var stopLoss = entryPrice - (lowestPriceChange * 2m);
                return Math.Round(Math.Max(0m, stopLoss), 2, MidpointRounding.AwayFromZero);
            }

            var percentage = fallbackStopLossPercent > 0m ? fallbackStopLossPercent : 0.5m;
            return Math.Round(entryPrice * (1m - percentage / 100m), 2, MidpointRounding.AwayFromZero);
        }

        public bool ValidateSell(TradeOrder trade, Candle currentCandle, out string outcome)
        {
            outcome = string.Empty;
            if (trade == null || currentCandle == null || trade.EntryPrice <= 0m || currentCandle.Close <= 0m)
                return false;

            if (trade.StopLoss <= 0m)
                trade.StopLoss = CalculateInitialStopLoss(
                    trade.EntryPrice,
                    trade.LowestPriceChange,
                    _settings.Current.StopLossPercent);

            // Hard monetary protection is evaluated on completed-candle processing too,
            // using estimated net P&L after estimated round-trip charges.
            var estimatedNetPnl = (currentCandle.Close - trade.EntryPrice) * trade.Quantity
                                  - trade.EstimatedCharges;
            if (estimatedNetPnl <= -_settings.Current.HardMonetaryStopLoss)
            {
                outcome = "HARD_MONETARY_STOP_LOSS";
                return true;
            }

            if (_settings.Current.EnableTargetExit && trade.TargetPrice > 0m && currentCandle.Close >= trade.TargetPrice)
            {
                outcome = "TARGET_EXIT";
                return true;
            }

            // Once activated, the trailing stop is evaluated against every live tick.
            if (_settings.Current.EnableTrailingStop && trade.TrailingActivated && trade.TrailingStopPrice > 0m && currentCandle.Close <= trade.TrailingStopPrice)
            {
                outcome = "TRAILING_STOP";
                return true;
            }

            // Preserve the protective initial stop for positions before trailing activates.
            if ((!_settings.Current.EnableTrailingStop || !trade.TrailingActivated) && trade.StopLoss > 0m && currentCandle.Close <= trade.StopLoss)
            {
                outcome = "STOP_LOSS";
                return true;
            }

            return false;
        }

        /// <summary>Processes an owned position from a live tick; no candle polling is involved.</summary>
        public async Task ProcessSellTickAsync(TradeOrder trade, decimal ltp, DateTime tickTime)
        {
            if (trade == null || ltp <= 0m || trade.Quantity <= 0 || trade.EntryPrice <= 0m)
                return;

            var estimatedNet = (ltp - trade.EntryPrice) * trade.Quantity - trade.EstimatedCharges;
            var changed = false;

            // Existing/reconciled positions may predate the configured stop-loss field.
            // Initialise and persist the same protective stop on their first live tick.
            if (trade.StopLoss <= 0m)
            {
                trade.StopLoss = CalculateInitialStopLoss(
                    trade.EntryPrice,
                    trade.LowestPriceChange,
                    _settings.Current.StopLossPercent);
                changed = trade.StopLoss > 0m;
                if (changed)
                {
                    _logger.LogInformation(
                        "INITIAL STOP LOSS BACKFILLED Symbol={Symbol} EntryPrice={EntryPrice:F4} LowestPriceChange={LowestPriceChange:F6} Multiplier={Multiplier:F2} StopLoss={StopLoss:F4}",
                        trade.Symbol, trade.EntryPrice,
                        trade.LowestPriceChange, 2m, trade.StopLoss);
                }
            }

            if (ltp > trade.HighestPriceSinceEntry)
            {
                trade.HighestPriceSinceEntry = ltp;
                changed = true;
            }

            var settings = _settings.Current;
            var targetEnabled = settings.EnableTargetExit && trade.TargetPrice > 0m;
            var trailingEnabled = settings.EnableTrailingStop;

            if (trailingEnabled)
            {
                // Trailing activates only after estimated round-trip net profit is positive.
                if (!trade.TrailingActivated && estimatedNet > 0m)
                {
                    trade.TrailingActivated = true;
                    trade.HighestPriceSinceEntry = Math.Max(trade.HighestPriceSinceEntry, ltp);
                    trade.TrailingStopPrice = Math.Max(trade.EntryPrice, trade.HighestPriceSinceEntry - Math.Max(trade.LowestPriceChange, 0.01m));
                    changed = true;
                }
                else if (trade.TrailingActivated)
                {
                    var nextStop = Math.Max(trade.EntryPrice, trade.HighestPriceSinceEntry - Math.Max(trade.LowestPriceChange, 0.01m));
                    if (nextStop > trade.TrailingStopPrice) { trade.TrailingStopPrice = nextStop; changed = true; }
                }
            }

            if (changed) await _tradeOrderFileRepository.UpdateAsync(trade);

            string? exitReason = null;
            if (estimatedNet <= -settings.HardMonetaryStopLoss)
                exitReason = "HARD_MONETARY_STOP_LOSS";
            else if (targetEnabled && ltp >= trade.TargetPrice)
                exitReason = "TARGET_EXIT";
            else if (trailingEnabled && trade.TrailingActivated && trade.TrailingStopPrice > 0m && ltp <= trade.TrailingStopPrice)
                exitReason = "TRAILING_STOP";
            else if (trade.StopLoss > 0m && ltp <= trade.StopLoss)
                exitReason = "STOP_LOSS";

            // Evaluate the VWAP close exit once per minute from the persisted completed
            // candle file. Live ticks continue to handle the protective stop/trailing stop.
            if (exitReason == null)
            {
                var currentMinute = new DateTime(tickTime.Year, tickTime.Month, tickTime.Day,
                    tickTime.Hour, tickTime.Minute, 0, DateTimeKind.Unspecified);
                if (!_lastVwapExitEvaluation.TryGetValue(trade.SymbolToken, out var evaluatedMinute) ||
                    evaluatedMinute < currentMinute)
                {
                    _lastVwapExitEvaluation[trade.SymbolToken] = currentMinute;
                    var interval = string.IsNullOrWhiteSpace(settings.CandleInterval)
                        ? "ONE_MINUTE" : settings.CandleInterval;
                    var completedCandles = await _candleStore.GetAsync(trade.SymbolToken, trade.Symbol, interval);
                    if (EntryStrategyService.TryGetVwapExitReason(completedCandles, out var sessionVwap, out var candleClose))
                    {
                        exitReason = "VWAP_CLOSE_EXIT";
                        _logger.LogInformation(
                            "VWAP EXIT SIGNAL Symbol={Symbol} Token={Token} CandleClose={CandleClose:F4} SessionVWAP={SessionVWAP:F6} TickLtp={Ltp:F4}",
                            trade.Symbol, trade.SymbolToken, candleClose, sessionVwap, ltp);
                    }
                }
            }

            if (exitReason != null)
            {
                if (exitReason == "HARD_MONETARY_STOP_LOSS")
                {
                    _logger.LogWarning(
                        "HARD MONETARY STOP LOSS triggered. Symbol={Symbol} Quantity={Quantity} EntryPrice={EntryPrice:F4} Ltp={Ltp:F4} EstimatedCharges={EstimatedCharges:F2} EstimatedNetPnl={EstimatedNetPnl:F2} Limit={Limit:F2}",
                        trade.Symbol, trade.Quantity, trade.EntryPrice, ltp,
                        trade.EstimatedCharges, estimatedNet, settings.HardMonetaryStopLoss);
                }

                var synthetic = new Candle { Timestamp = tickTime, Open = ltp, High = ltp, Low = ltp, Close = ltp };
                await PlaceSellOrder(new Dictionary<string, TradeOrder> { [trade.Symbol] = trade },
                    new Dictionary<string, List<Candle>> { [trade.SymbolToken] = new() { synthetic } },
                    forceSquareOff: false,
                    forcedExitReason: exitReason);
            }
        }

        /// <summary>Evaluates and processes all owned positions concurrently.</summary>
        public async Task PlaceSellOrder(
            Dictionary<string, TradeOrder> ownedStocks,
            Dictionary<string, List<Candle>> candles,
            bool forceSquareOff = false,
            string? forcedExitReason = null)
        {
            if (ownedStocks == null || ownedStocks.Count == 0 || candles == null || candles.Count == 0)
                return;

            await Task.WhenAll(ownedStocks.Values
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SymbolToken))
                .Select(x => ProcessSellAsync(x, candles, forceSquareOff, forcedExitReason)));
        }

        private async Task ProcessSellAsync(
            TradeOrder trade,
            Dictionary<string, List<Candle>> candles,
            bool forceSquareOff,
            string? forcedExitReason = null)
        {
            try
            {
                if (!candles.TryGetValue(trade.SymbolToken, out var stockCandles)
                    || stockCandles == null || stockCandles.Count == 0)
                    return;

                var candle = stockCandles.OrderBy(x => x.Timestamp).Last();
                var outcome = forcedExitReason ?? (forceSquareOff ? "SQUARE_OFF" : string.Empty);
                if (!forceSquareOff && forcedExitReason == null && !ValidateSell(trade, candle, out outcome))
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

                    await _quotePublisher.SetOwnedAsync(trade.SymbolToken, 0);
                    await _tradeOrderFileRepository.DeleteAsync(trade.Symbol);
                    _logger.LogInformation("SELL ORDER SUBMITTED Symbol={Symbol} Outcome={Outcome} OrderId={OrderId}", trade.Symbol, outcome, response.Data.OrderId);
                    return;
                }

                trade.ExitPrice = exitPrice;
                trade.ExitTime = candle.Timestamp;
                trade.GrossProfit = (exitPrice - trade.EntryPrice) * trade.Quantity;
                trade.NetProfit = trade.GrossProfit - trade.EstimatedCharges;

                await _tradeCloseStore.AppendAsync(trade);
                await _quotePublisher.SetOwnedAsync(trade.SymbolToken, 0);
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

        /// <summary>
        /// Loads historical candles with a warm-up backfill even when today's candles are already cached.
        /// The cache is merged by CandleFileStoreService; callers can calculate indicators over prior sessions.
        /// </summary>
        /// <summary>
        /// Downloads an independent historical candle sample for EMA calibration.
        /// Small date windows avoid provider maximum-range limits and bypass the candle cache,
        /// so the workbook captures the actual response from the historical endpoint.
        /// </summary>
        public async Task<List<Candle>> DownloadCandlesForEmaCalibrationAsync(
            string token,
            string jwtToken,
            string exchange,
            string interval,
            DateTime fromDate,
            DateTime toDate,
            int chunkDays = 5,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Instrument token is required.", nameof(token));
            if (toDate < fromDate)
                throw new ArgumentException("toDate must be greater than or equal to fromDate.");
            if (chunkDays < 1 || chunkDays > 30)
                throw new ArgumentOutOfRangeException(nameof(chunkDays), "Chunk size must be between 1 and 30 days.");

            var all = new List<Candle>();
            var cursor = fromDate;
            while (cursor <= toDate)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var chunkEnd = cursor.Date.AddDays(chunkDays).AddTicks(-1);
                if (chunkEnd > toDate) chunkEnd = toDate;

                List<Candle> chunk;
                try
                {
                    chunk = await DownloadCandleWithRateLimitAsync(
                        token, jwtToken, interval, cursor, chunkEnd,
                        string.IsNullOrWhiteSpace(exchange) ? "NSE" : exchange);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex,
                        "EMA calibration download stopped at failed chunk. Token={Token}, From={From}, To={To}, DownloadedSoFar={Count}",
                        token, cursor, chunkEnd, all.Count);
                    break; // Preserve partial data so the workbook can explain the coverage gap.
                }
                if (chunk.Count > 0) all.AddRange(chunk);

                _logger.LogInformation(
                    "EMA calibration download: Token={Token}, Interval={Interval}, From={From}, To={To}, Returned={Count}",
                    token, interval, cursor, chunkEnd, chunk.Count);

                cursor = chunkEnd.AddMinutes(1);
            }

            return all
                .Where(c => c.Close > 0m && c.High >= c.Low)
                .GroupBy(c => c.Timestamp)
                .Select(g => g.Last())
                .OrderBy(c => c.Timestamp)
                .ToList();
        }

        public async Task<Dictionary<string, List<Candle>>> DownloadCandlesWithHistoricalWarmupAsync(
            List<MarketQuoteData> quotes,
            List<Instrument> instruments,
            string jwtToken,
            string interval,
            DateTime fromDate,
            DateTime toDate,
            int batchDelaySeconds = 1)
        {
            var result = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
            if (quotes == null || quotes.Count == 0 || instruments == null || instruments.Count == 0)
                return result;

            var byToken = instruments
                .Where(x => !string.IsNullOrWhiteSpace(x.token))
                .GroupBy(x => x.token.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            var duration = CandleFileStoreService.IntervalDuration(interval);

            foreach (var quote in quotes.Where(q => !string.IsNullOrWhiteSpace(q.SymbolToken)))
            {
                var token = quote.SymbolToken.Trim();
                if (!byToken.TryGetValue(token, out var instrument))
                    continue;

                var exchange = string.IsNullOrWhiteSpace(instrument.exch_seg)
                    ? "NSE"
                    : instrument.exch_seg.Trim().ToUpperInvariant();
                var cached = await _candleStore.GetAsync(token, instrument.symbol, interval);
                var merged = cached.OrderBy(c => c.Timestamp).ToList();
                var rateLimited = false;

                try
                {
                    // Backfill the gap before the earliest cached candle. This is essential:
                    // a cache containing today's latest candle must not suppress prior-session warm-up.
                    var earliest = merged.Count == 0 ? toDate : merged.Min(c => c.Timestamp);
                    if (merged.Count == 0 || earliest > fromDate)
                    {
                        var warmupTo = merged.Count == 0 ? toDate : earliest.Subtract(duration);
                        if (warmupTo >= fromDate)
                        {
                            var warmup = await DownloadCandleWithRateLimitAsync(
                                token, jwtToken, interval, fromDate, warmupTo, exchange);
                            if (warmup.Count > 0)
                                await _candleStore.SaveAsync(token, instrument.symbol, interval, warmup);
                        }
                    }

                    // Fill any missing tail after the latest cached candle.
                    merged = (await _candleStore.GetAsync(token, instrument.symbol, interval))
                        .OrderBy(c => c.Timestamp).ToList();
                    var latest = merged.Count == 0 ? fromDate.Subtract(duration) : merged.Max(c => c.Timestamp);
                    var fetchFrom = latest.Add(duration) > fromDate ? latest.Add(duration) : fromDate;
                    if (fetchFrom <= toDate)
                    {
                        var tail = await DownloadCandleWithRateLimitAsync(
                            token, jwtToken, interval, fetchFrom, toDate, exchange);
                        if (tail.Count > 0)
                            await _candleStore.SaveAsync(token, instrument.symbol, interval, tail);
                    }

                    merged = (await _candleStore.GetAsync(token, instrument.symbol, interval))
                        .Where(c => c.Close > 0 && c.High >= c.Low)
                        .GroupBy(c => c.Timestamp)
                        .Select(g => g.Last())
                        .OrderBy(c => c.Timestamp)
                        .ToList();
                    result[token] = merged;
                    UpdateQuoteIndicators(quote, merged, interval);
                }
                catch (Exception ex)
                {
                    rateLimited = IsHistoricalRateLimitException(ex);
                    _logger.LogWarning(ex,
                        "Historical warm-up load failed for {Symbol} token {Token}. Using cached candles only. RateLimited={RateLimited}",
                        instrument.symbol, token, rateLimited);
                    result[token] = merged;
                }

                await _webSocket.SubscribeAsync([token], exchange);
                if (rateLimited)
                {
                    _logger.LogWarning("Stopping historical warm-up downloads after provider rate limit. Remaining symbols will use cached data.");
                    break;
                }

                if (batchDelaySeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(batchDelaySeconds));
            }

            return result;
        }

    }
}
