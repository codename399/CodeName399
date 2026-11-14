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

        private readonly TradeCloseHistoryStore _tradeCloseStore;

        private readonly CandleFileStoreService _candleStore;

        protected readonly IBrokerageCalculatorService _brokerage;

        private static readonly TimeZoneInfo IndianTimeZone =
    TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows()
            ? "India Standard Time"
            : "Asia/Kolkata");

        private readonly SemaphoreSlim _quoteSemaphore = new(2, 2);
        private readonly SemaphoreSlim _buyExecutionSemaphore = new(1, 1);
        // A symbol remains reserved from buy evaluation through the life of its open trade.
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _symbolLocks =
            new(StringComparer.OrdinalIgnoreCase);
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
        IBrokerageCalculatorService brokerage,
        TradeCloseHistoryStore tradeCloseStore,
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
            _brokerage = brokerage;
            _tradeCloseStore = tradeCloseStore;
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

            // Broker net positions are the final source of truth for shares already held,
            // including positions that were opened outside this process or missed by recovery.
            var brokerPositions = await _angel.GetPositionsAsync();
            if (_angel.LastPositionRefreshSucceeded)
            {
                foreach (var position in brokerPositions.Where(x => x.NetQty > 0 && !string.IsNullOrWhiteSpace(x.TradingSymbol)))
                {
                    var symbol = position.TradingSymbol.Trim().ToUpperInvariant();
                    if (owned.ContainsKey(symbol)) continue;
                    owned[symbol] = new TradeOrder
                    {
                        Symbol = symbol,
                        SymbolToken = position.SymbolToken,
                        Exchange = string.IsNullOrWhiteSpace(position.Exchange) ? "NSE" : position.Exchange,
                        Quantity = position.NetQty,
                        EntryPrice = position.BuyAvgPrice,
                        EntryTime = GetIndianTime(),
                        ExecutionMode = "Live"
                    };
                }
            }
            else
            {
                _logger.LogWarning("Broker positions refresh failed; owned-stock check is relying on persisted trades and order websocket state.");
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

            var quotes = new List<MarketQuoteData>();
            foreach (var request in requests)
            {
                // A provider 403 activates the shared endpoint cooldown in RequestHelper.
                // Stop issuing quote requests for the rest of this scan instead of
                // queuing dozens of requests that cannot succeed during the cooldown.
                if (RequestHelper.IsEndpointCoolingDown(AngelApiUrlConstant.marketDataUrl))
                {
                    _logger.LogWarning(
                        "QUOTE SCAN PAUSED: Angel One market-quote endpoint is cooling down. RemainingBatches={RemainingBatches}",
                        requests.Count - requests.IndexOf(request));
                    break;
                }

                var batch = await GetBatchAsync(request.Tokens, jwt, request.Exchange);
                quotes.AddRange(batch);
            }

            return quotes;
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
                .Where(x => x.Ltp <= Math.Max(0m, _settings.Current.MinimumStockPrice) && x.Close > 0m && x.Ltp > x.Close)
            .Where(x => GetActivityVolume(x) > 0L)
            .OrderByDescending(GetActivityVolume)
            .ThenByDescending(x => (x.Ltp - x.Close) / x.Close)
            .ThenByDescending(x => x.Open > 0m ? (x.Ltp - x.Open) / x.Open : decimal.MinValue)
            .ThenBy(x => x.Ltp)
            .Take(Math.Max(0, count))
            .ToList();
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

            quote.Ema9 = Math.Round(EmaCalculator.Calculate(orderedCandles.Select(c => c.Close).ToList(), 9), 6, MidpointRounding.AwayFromZero);
            quote.Ema21 = Math.Round(EmaCalculator.Calculate(orderedCandles.Select(c => c.Close).ToList(), 21), 6, MidpointRounding.AwayFromZero);
            quote.PreviousCandleEma9 = Math.Round(EmaCalculator.Calculate(previousCandles.Select(c => c.Close).ToList(), 9), 6, MidpointRounding.AwayFromZero);
            quote.PreviousCandleEma21 = Math.Round(EmaCalculator.Calculate(previousCandles.Select(c => c.Close).ToList(), 21), 6, MidpointRounding.AwayFromZero);
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

        public async Task PlaceBuyOrders(HashSet<MarketQuoteData> quotes, Dictionary<string, List<Candle>> candles, string jwtToken)
        {
            foreach (var batch in quotes.Chunk(_settings.Current.BuyBatchSize))
            {
                var batchTasks = batch.Select(async quote =>
                {
                    await _buyExecutionSemaphore.WaitAsync();
                    var symbol = (quote.TradingSymbol ?? string.Empty).Trim().ToUpperInvariant();
                    var symbolLockRetained = false;
                    var symbolLockAcquired = false;
                    try
                    {
                        if (string.IsNullOrWhiteSpace(symbol) || !_symbolLocks.TryAdd(symbol, 0))
                        {
                            _logger.LogInformation("BUY SKIPPED Symbol={Symbol} Reason=SymbolAlreadyLocked", quote.TradingSymbol);
                            return;
                        }
                        symbolLockAcquired = true;

                        var openTrades = await GetOwnedStocksAsync();
                        var alreadyOwned = openTrades.Values.Any(t =>
                            string.Equals(t.Symbol, symbol, StringComparison.OrdinalIgnoreCase)
                            || (!string.IsNullOrWhiteSpace(quote.SymbolToken)
                                && string.Equals(t.SymbolToken, quote.SymbolToken, StringComparison.OrdinalIgnoreCase)));
                        if (alreadyOwned)
                        {
                            _logger.LogInformation("BUY SKIPPED Symbol={Symbol} Token={Token} Reason=AlreadyOwned", symbol, quote.SymbolToken);
                            return;
                        }
                        var maxTrades = Math.Max(1, _settings.Current.MaxTrades);
                        if (openTrades.Count >= maxTrades)
                        {
                            _logger.LogInformation("BUY SKIPPED Symbol={Symbol} Reason=MaxTradesReached OpenTrades={OpenTrades} MaxTrades={MaxTrades}", quote.TradingSymbol, openTrades.Count, maxTrades);
                            return;
                        }
                        var quoteCandles = candles.TryGetValue(quote.SymbolToken, out var loadedCandles)
                            ? loadedCandles
                            : new List<Candle>();

                        // Entry is evaluated only against completed candles for an EMA9/EMA21 bullish crossover.
                        var entrySatisfied = EntryStrategyService.TryGetEntryReason(
                            quote, quoteCandles, out var entryReason, out var entryDiagnostics);
                        var diagnosticCandles = quoteCandles
                            .Where(c => c != null && c.Close > 0m)
                            .GroupBy(c => c.Timestamp)
                            .Select(g => g.Last())
                            .OrderBy(c => c.Timestamp)
                            .ToList();
                        var previousCandleClose = diagnosticCandles.Count > 1
                            ? diagnosticCandles[^2].Close
                            : 0m;

                        _logger.LogInformation(
                            "ENTRY STRATEGY EVALUATION IST={EvaluationTime:yyyy-MM-dd HH:mm:ss.fff} Symbol={Symbol} Token={Token} LTP={Ltp:F4} QuoteTimeUtc={QuoteTimeUtc:O} CandleCount={ValidCandlesLoaded} PreviousCandleTime={PreviousCandleTime:yyyy-MM-dd HH:mm:ss} PreviousCandleClose={PreviousCandleClose:F4} PreviousEMA9={PreviousEMA9:F6} PreviousEMA21={PreviousEMA21:F6} PreviousGap={PreviousGap:F6} LastCandleTime={LastCandleTime:yyyy-MM-dd HH:mm:ss} LastClose={LastClose:F4} EMA9={EMA9:F6} EMA21={EMA21:F6} CurrentGap={CurrentGap:F6} CrossoverDetected={CrossoverDetected} EntryConditionSatisfied={EntryConditionSatisfied} EntryRejectionReason={EntryRejectionReason}",
                            GetIndianTime(), quote.TradingSymbol, quote.SymbolToken, quote.Ltp,
                            quote.QuoteTimeUtc, entryDiagnostics.ValidCandlesLoaded,
                            entryDiagnostics.PreviousCandleTime,
                            previousCandleClose,
                            entryDiagnostics.PreviousEma9, entryDiagnostics.PreviousEma21,
                            (entryDiagnostics.PreviousEma9 ?? 0m) - (entryDiagnostics.PreviousEma21 ?? 0m),
                            entryDiagnostics.LastCandleTime, entryDiagnostics.LastClose,
                            entryDiagnostics.Ema9, entryDiagnostics.Ema21,
                            (entryDiagnostics.Ema9 ?? 0m) - (entryDiagnostics.Ema21 ?? 0m),
                            entryDiagnostics.EntryConditionSatisfied,
                            entryDiagnostics.EntryConditionSatisfied,
                            entryDiagnostics.EntryRejectionReason);

                        if (!entrySatisfied)
                        {
                            _logger.LogDebug(
                                "BUY SKIPPED after EMA9/EMA21 crossover evaluation. Symbol={Symbol} Token={Token} Reason={Reason}",
                                quote.TradingSymbol, quote.SymbolToken, entryDiagnostics.EntryRejectionReason);
                            return;
                        }

                        _logger.LogInformation(
                            "BUY ELIGIBILITY PASSED Symbol={Symbol} Token={Token} EntryReason={EntryReason}; starting quantity, margin and profitability checks.",
                            quote.TradingSymbol, quote.SymbolToken, entryReason);

                        var (quantity, margin, ltp) = await GetQuantitesAndMargin(quote, quoteCandles);

                        if (quantity <= 0)
                        {
                            _logger.LogWarning(
                                "BUY NOT PLACED Symbol={Symbol} Token={Token} Gate=QuantityAndProfitability Reason=QuantityCalculationReturnedZero",
                                quote.TradingSymbol, quote.SymbolToken);
                        }

                        if (quantity > 0)
                        {
                            quote.Quantity = quantity;
                            quote.TotalInvestment = margin;
                            quote.Ltp = ltp;

                            var tradeOrder = MarketQuoteData.ToTradeOrder(quote, _settings.Current.EnableAutoTrading);
                            tradeOrder.EntryReason = entryReason;
                            tradeOrder.StopLoss = CalculateInitialStopLoss(
                                tradeOrder.EntryPrice, 0m, 1.0m);
                            _logger.LogInformation(
                                "INITIAL HARD STOP LOSS SET Symbol={Symbol} EntryPrice={EntryPrice:F4} StopLossPercent={StopLossPercent:F2} StopLoss={StopLoss:F4}",
                                tradeOrder.Symbol, tradeOrder.EntryPrice, 1.0m, tradeOrder.StopLoss);
                            tradeOrder.TargetPrice = tradeOrder.EntryPrice + tradeOrder.LowestPriceChange;
                            tradeOrder.HighestPriceSinceEntry = tradeOrder.EntryPrice;
                            tradeOrder.TrailingActivated = false;
                            tradeOrder.TrailingStopPrice = tradeOrder.TargetPrice;

                            _logger.LogInformation(
                                "BUY DECISION ACCEPTED IST={Time:yyyy-MM-dd HH:mm:ss.fff} Symbol={Symbol} Token={Token} Quantity={Quantity} EntryPrice={EntryPrice:F4} Margin={Margin:F2} StopLoss={StopLoss:F4} TargetPrice={TargetPrice:F4} AutoTrading={AutoTrading} ExecutionMode={ExecutionMode} EntryReason={EntryReason}",
                                GetIndianTime(), tradeOrder.Symbol, tradeOrder.SymbolToken,
                                tradeOrder.Quantity, tradeOrder.EntryPrice, tradeOrder.TotalInvestment,
                                tradeOrder.StopLoss, tradeOrder.TargetPrice,
                                _settings.Current.EnableAutoTrading, tradeOrder.ExecutionMode, tradeOrder.EntryReason);

                            if (_settings.Current.EnableAutoTrading)
                            {
                                // Persist the intended position before the broker side effect.
                                // If the request times out, retaining this record prevents a duplicate buy
                                // while broker/order-websocket reconciliation determines whether it filled.
                                await _tradeOrderFileRepository.SaveAsync(tradeOrder);
                                symbolLockRetained = true;

                                var orderTag = $"CN399-BUY-{Guid.NewGuid():N}"[..19];
                                PlaceOrderResponse buyResponse;
                                try
                                {
                                    _logger.LogInformation(
                                        "LIVE BUY ORDER REQUEST IST={Time:yyyy-MM-dd HH:mm:ss.fff} Symbol={Symbol} Token={Token} Quantity={Quantity} Exchange={Exchange} Product=INTRADAY OrderType=MARKET Tag={OrderTag}",
                                        GetIndianTime(), tradeOrder.Symbol, tradeOrder.SymbolToken,
                                        tradeOrder.Quantity, tradeOrder.Exchange, orderTag);
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
                                    symbolLockRetained = false;
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
                                symbolLockRetained = true;
                            }

                            EmailHelper.SendEmail(
                        EmailHelper.BuildBuyOrderEmail(tradeOrder),
                        _settings.Current,
                        $"{tradeOrder.ExecutionMode} EXECUTED {tradeOrder.Symbol}",
                        true);
                        }
                    }
                    finally
                    {
                        if (symbolLockAcquired && !symbolLockRetained)
                            _symbolLocks.TryRemove(symbol, out _);
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
                quote, maximumMarketQuantity, availableBalance, referencePrice);

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
            var executionPrice = referencePrice;
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
            var requiredProfitPercent = Math.Max(0m, _settings.Current.RequiredProfitPercent);
            var requiredSellValue = buyValue + totalCharges + (buyValue * requiredProfitPercent / 100m);

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
            int maximumMarketQuantity,
            decimal availableBalance,
            decimal marginalPrice)
        {
            if (maximumMarketQuantity <= 0 || availableBalance <= 0m || marginalPrice <= 0m)
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
                var estimatedBuyVwap = marginalPrice;

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
            var candleCount = Math.Clamp(_settings.Current.ChargeCoverLowestPriceChangeCandles, 1, 100);
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

        private static decimal CalculateInitialStopLoss(
            decimal entryPrice,
            decimal lowestPriceChange,
            decimal fallbackStopLossPercent)
        {
            if (entryPrice <= 0m)
                return 0m;

            // Fixed hard protective stop: 1% below entry, independent of candle movement.
            return Math.Round(entryPrice * 0.99m, 2, MidpointRounding.AwayFromZero);
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
                    1.0m);

            if (!_settings.Current.EnableTrailingStop && trade.TargetPrice > 0m && currentCandle.Close >= trade.TargetPrice)
            {
                outcome = "LOWEST_PRICE_CHANGE_TARGET";
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

            var changed = false;

            // Existing/reconciled positions may predate the configured stop-loss field.
            // Initialise and persist the same protective stop on their first live tick.
            if (trade.StopLoss <= 0m)
            {
                trade.StopLoss = CalculateInitialStopLoss(
                    trade.EntryPrice,
                    trade.LowestPriceChange,
                    1.0m);
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
            var trailingEnabled = settings.EnableTrailingStop;
            var targetEnabled = !trailingEnabled && trade.TargetPrice > 0m;

            if (trailingEnabled)
            {
                // Activate once price reaches entry + the conservative candle move.
                // Thereafter trail upward by the same move; never lower the stop.
                var activationPrice = trade.EntryPrice + Math.Max(trade.LowestPriceChange, 0.01m);
                if (!trade.TrailingActivated && ltp >= activationPrice)
                {
                    trade.TrailingActivated = true;
                    trade.TrailingStopPrice = activationPrice;
                    changed = true;
                }
                else if (trade.TrailingActivated)
                {
                    var nextStop = Math.Max(activationPrice, trade.HighestPriceSinceEntry - Math.Max(trade.LowestPriceChange, 0.01m));
                    if (nextStop > trade.TrailingStopPrice) { trade.TrailingStopPrice = nextStop; changed = true; }
                }
            }

            if (changed) await _tradeOrderFileRepository.UpdateAsync(trade);

            string? exitReason = null;
            if (trailingEnabled && trade.TrailingActivated && trade.TrailingStopPrice > 0m && ltp <= trade.TrailingStopPrice)
                exitReason = "TRAILING_STOP";
            else if (!trailingEnabled && targetEnabled && ltp >= trade.TargetPrice)
                exitReason = "LOWEST_PRICE_CHANGE_TARGET";
            else if (trade.StopLoss > 0m && ltp <= trade.StopLoss)
                exitReason = "STOP_LOSS";

            if (exitReason != null)
            {
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

                    await _tradeOrderFileRepository.DeleteAsync(trade.Symbol);
                    _symbolLocks.TryRemove(trade.Symbol, out _);
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
                _symbolLocks.TryRemove(trade.Symbol, out _);

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
