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
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (MarketQuoteData Quote, List<Candle> Candles, decimal EntryPrice)> _entryWatchlist =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _entryTickInProgress =
            new(StringComparer.OrdinalIgnoreCase);

        // Remembers the last quantity/entry-price combination rejected by the
        // one-paisa-after-charges gate. Re-evaluate only after market depth or
        // entry price improves, preventing repeated margin/charge API work on
        // identical matching ticks.
        private sealed record RejectedQuantityState(int Quantity, decimal EntryPrice);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, RejectedQuantityState> _rejectedQuantities =
            new(StringComparer.OrdinalIgnoreCase);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _lastVwapExitEvaluation =
            new(StringComparer.OrdinalIgnoreCase);

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
            _webSocket.TickReceived += OnMarketTickReceived;
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

        /// <summary>
        /// Reads finalized candles written by CandleBuilder from the shared candle directory.
        /// Kite.API never downloads or constructs historical candles.
        /// </summary>
        public async Task<Dictionary<string, List<Candle>>> LoadCandlesFromCandleBuilderAsync(
            List<MarketQuoteData> quotes,
            string interval,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
            if (quotes == null || quotes.Count == 0) return result;

            foreach (var quote in quotes.Where(q => !string.IsNullOrWhiteSpace(q.SymbolToken)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var token = quote.SymbolToken.Trim();
                var candles = await _candleStore.GetAsync(token, quote.TradingSymbol, interval, cancellationToken);
                result[token] = candles;

                _logger.LogInformation(
                    "CANDLEBUILDER CANDLES LOADED Symbol={Symbol} Token={Token} Interval={Interval} Count={Count} Latest={Latest}",
                    quote.TradingSymbol, token, interval, candles.Count,
                    candles.Count > 0 ? candles[^1].Timestamp : null);

                if (candles.Count < 10)
                    _logger.LogWarning(
                        "ENTRY CANDLES INSUFFICIENT Symbol={Symbol} Token={Token} Required={Required} Available={Available} Source=CandleBuilder",
                        quote.TradingSymbol, token, 10, candles.Count);
            }

            return result;
        }

        /// <summary>Reads CandleBuilder files for currently owned positions.</summary>
        public async Task<Dictionary<string, List<Candle>>> LoadOwnedCandlesFromCandleBuilderAsync(
            Dictionary<string, TradeOrder> ownedStocks,
            string interval,
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<string, List<Candle>>(StringComparer.OrdinalIgnoreCase);
            if (ownedStocks == null || ownedStocks.Count == 0) return result;

            foreach (var trade in ownedStocks.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(trade.SymbolToken)) continue;
                var token = trade.SymbolToken.Trim();
                result[token] = await _candleStore.GetAsync(token, trade.Symbol, interval, cancellationToken);
            }

            return result;
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
                .Where(x => x.Ltp > 0m && x.Ltp < _settings.Current.MinimumStockPrice)
                .Where(x => GetActivityVolume(x) > 0L)
                .OrderByDescending(GetActivityVolume)
                .ThenBy(x => x.Ltp)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Configures entry watches using the candles loaded from CandleBuilder.
        /// Completed candles are used to initialize entry levels; live WebSocket ticks evaluate them.
        /// </summary>
        /// <summary>
        /// Removes all pending entry watches, for example when the market closes
        /// or the configured entry window has ended.
        /// </summary>
        public void ClearEntryWatchlist(string reason)
        {
            var removed = _entryWatchlist.Count;
            _entryWatchlist.Clear();
            _entryTickInProgress.Clear();
            _rejectedQuantities.Clear();

            _logger.LogInformation(
                "ENTRY WATCHLIST CLEARED IST={Time:yyyy-MM-dd HH:mm:ss} Reason={Reason} Removed={Removed}",
                GetIndianTime(), reason, removed);
        }

        public void ConfigureEntryWatchlist(IEnumerable<MarketQuoteData> quotes, Dictionary<string, List<Candle>> candles)
        {
            var refreshed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var quote in quotes)
            {
                if (string.IsNullOrWhiteSpace(quote.SymbolToken)) continue;
                var stockCandles = candles.TryGetValue(quote.SymbolToken, out var loaded)
                    ? loaded : new List<Candle>();
                var completed = stockCandles
                    .Where(c => c != null && c.Low > 0m && c.Close > 0m)
                    .GroupBy(c => c.Timestamp)
                    .Select(g => g.Last())
                    .OrderBy(c => c.Timestamp)
                    .TakeLast(10)
                    .ToList();

                if (completed.Count < 10)
                {
                    _entryWatchlist.TryRemove(quote.SymbolToken, out _);
                    _logger.LogWarning("ENTRY WATCH NOT REGISTERED Symbol={Symbol} Token={Token} Reason=InsufficientCandles Count={Count}",
                        quote.TradingSymbol, quote.SymbolToken, completed.Count);
                    continue;
                }

                var entryPrice = completed.Min(c => c.Low);
                _entryWatchlist[quote.SymbolToken] = (quote, stockCandles, entryPrice);
                refreshed.Add(quote.SymbolToken);
                _logger.LogInformation("ENTRY WATCH UPDATED IST={Time:yyyy-MM-dd HH:mm:ss} Symbol={Symbol} Token={Token} EntryPrice={EntryPrice:F2} CandleCount={CandleCount}",
                    GetIndianTime(), quote.TradingSymbol, quote.SymbolToken, entryPrice, stockCandles.Count);
            }

            foreach (var token in _entryWatchlist.Keys)
                if (!refreshed.Contains(token))
                    _entryWatchlist.TryRemove(token, out _);
        }

        private void OnMarketTickReceived(object? sender, MarketTick tick)
        {
            _ = ProcessEntryTickAsync(tick);
        }

        private async Task ProcessEntryTickAsync(MarketTick tick)
        {
            if (tick == null || tick.Ltp <= 0m ||
                !_entryWatchlist.TryGetValue(tick.SymbolToken, out var watch))
                return;

            // Keep the configured exact-price condition, evaluated on every tick.
            if (tick.Ltp != watch.EntryPrice)
                return;

            if (!_entryTickInProgress.TryAdd(tick.SymbolToken, 0))
                return;

            try
            {
                var owned = await GetOwnedStocksAsync();
                if (owned.ContainsKey(watch.Quote.TradingSymbol))
                {
                    _entryWatchlist.TryRemove(tick.SymbolToken, out _);
                    return;
                }

                var quote = watch.Quote;
                quote.Ltp = tick.Ltp;
                quote.Bid = tick.Bid > 0m ? tick.Bid : quote.Bid;
                quote.Ask = tick.Ask > 0m ? tick.Ask : quote.Ask;
                quote.LastTradedQuantity = tick.LastTradedQuantity > 0
                    ? tick.LastTradedQuantity : quote.LastTradedQuantity;
                quote.DayVolume = tick.DayVolume > 0 ? tick.DayVolume : quote.DayVolume;
                quote.Volume = tick.Volume > 0 ? tick.Volume : quote.Volume;
                if (tick.AskDepth.Count > 0 || tick.BidDepth.Count > 0)
                {
                    quote.Depth = new MarketQuoteDepth
                    {
                        Buy = tick.BidDepth.Select(x => new MarketQuoteDepthLevel
                        {
                            Price = x.Price, Quantity = x.Quantity, Orders = x.OrderCount
                        }).ToList(),
                        Sell = tick.AskDepth.Select(x => new MarketQuoteDepthLevel
                        {
                            Price = x.Price, Quantity = x.Quantity, Orders = x.OrderCount
                        }).ToList()
                    };
                }

                _logger.LogInformation("ENTRY TICK MATCH IST={Time:yyyy-MM-dd HH:mm:ss.fff} Symbol={Symbol} Token={Token} TickLtp={Ltp:F2} CandleEntryPrice={EntryPrice:F2}",
                    GetIndianTime(), quote.TradingSymbol, tick.SymbolToken, tick.Ltp, watch.EntryPrice);

                var login = await _angel.GetLoginDataAsync();
                await PlaceBuyOrders(new HashSet<MarketQuoteData> { quote }, new Dictionary<string, List<Candle>>
                {
                    [tick.SymbolToken] = watch.Candles
                }, login.JwtToken);

                var refreshedOwned = await GetOwnedStocksAsync();
                if (refreshedOwned.ContainsKey(quote.TradingSymbol))
                    _entryWatchlist.TryRemove(tick.SymbolToken, out _);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Tick-driven entry processing failed. Symbol={Symbol} Token={Token}",
                    watch.Quote.TradingSymbol, tick.SymbolToken);
            }
            finally
            {
                _entryTickInProgress.TryRemove(tick.SymbolToken, out _);
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

                        // Re-evaluate the lowest low of the latest ten completed candles on every candle close.
                        var entrySatisfied = EntryStrategyService.TryGetEntryReason(
                            quote, quoteCandles, out var entryPrice, out var entryReason);
                        _logger.LogInformation(
                            "ENTRY PRICE EVALUATION IST={Time:yyyy-MM-dd HH:mm:ss.fff} Symbol={Symbol} Token={Token} LTP={Ltp:F2} EntryPrice={EntryPrice:F2} CandleCount={CandleCount} EntryConditionSatisfied={Satisfied} Reason={Reason}",
                            GetIndianTime(), quote.TradingSymbol, quote.SymbolToken, quote.Ltp, entryPrice, quoteCandles.Count, entrySatisfied, entryReason);
                        if (!entrySatisfied) return;

                        var (quantity, margin, ltp, productType) = await GetQuantitesAndMargin(quote, quoteCandles, entryPrice);

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
                            tradeOrder.ProductType = productType;
                            tradeOrder.EntryPrice = entryPrice;
                            tradeOrder.StopLoss = Math.Round(entryPrice - 0.01m, 2, MidpointRounding.AwayFromZero);
                            tradeOrder.TargetPrice = Math.Round(entryPrice + 0.01m, 2, MidpointRounding.AwayFromZero);

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

                                var orderTag = $"CN399-BUY-{Guid.NewGuid():N}"[..19];
                                PlaceOrderResponse buyResponse;
                                try
                                {
                                    _logger.LogInformation(
                                        "LIVE BUY ORDER REQUEST IST={Time:yyyy-MM-dd HH:mm:ss.fff} Symbol={Symbol} Token={Token} Quantity={Quantity} Exchange={Exchange} Product={ProductType} OrderType=MARKET Tag={OrderTag}",
                                        GetIndianTime(), tradeOrder.Symbol, tradeOrder.SymbolToken,
                                        tradeOrder.Quantity, tradeOrder.Exchange, tradeOrder.ProductType, orderTag);
                                    buyResponse = await _angel.PlaceOrderAsync(
                                        new PlaceOrderRequest
                                        {
                                            TradingSymbol = tradeOrder.Symbol,
                                            SymbolToken = tradeOrder.SymbolToken,
                                            Exchange = tradeOrder.Exchange,
                                            TransactionType = "BUY",
                                            Quantity = tradeOrder.Quantity,
                                            OrderType = "MARKET",
                                            ProductType = tradeOrder.ProductType,
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

        private async Task<(int Quantity, decimal Margin, decimal Ltp, string ProductType)> GetQuantitesAndMargin(
            MarketQuoteData quote, List<Candle> candles, decimal entryPrice)
        {
            if (entryPrice <= 0m) return (0, 0m, 0m, "INTRADAY");

            // Prefer the calculated marginal price for INTRADAY. If it is unavailable,
            // use the strategy's entry price and execute both sides as DELIVERY.
            var marginalPrice = quote.Ltp;
            var productType = "INTRADAY";
            if (marginalPrice <= 0m)
            {
                marginalPrice = entryPrice;
                productType = "DELIVERY";
                _logger.LogWarning(
                    "MARGINAL PRICE FALLBACK Symbol={Symbol} Token={Token} MarginalPrice=0 EntryPrice={EntryPrice:F4} ProductType=DELIVERY",
                    quote.TradingSymbol, quote.SymbolToken, entryPrice);
            }

            // q2: sum displayed ask quantity available at or below the planned entry price.
            var askLevels = GetValidAskLevels(quote);
            var q2Long = askLevels.Where(x => x.Price <= entryPrice).Sum(x => (long)x.Quantity);
            var q2 = (int)Math.Clamp(q2Long, 0L, int.MaxValue);
            if (q2 <= 0)
            {
                _logger.LogDebug("QUANTITY CHECK SKIPPED Symbol={Symbol} Reason=NoMarketQuantityAtEntryPrice EntryPrice={EntryPrice:F2}", quote.TradingSymbol, entryPrice);
                return (0, 0m, 0m, productType);
            }

            // If the last attempt was rejected for insufficient profit after
            // charges, do not repeat margin and brokerage calculations until
            // either available market quantity or the entry price improves.
            if (_rejectedQuantities.TryGetValue(quote.SymbolToken, out var rejected)
                && q2 >= rejected.Quantity
                && entryPrice >= rejected.EntryPrice)
            {
                _logger.LogDebug(
                    "QUANTITY REJECTION UNCHANGED Symbol={Symbol} Token={Token} CurrentMarketQuantity={CurrentQuantity} RejectedQuantity={RejectedQuantity} CurrentEntryPrice={EntryPrice:F4} RejectedEntryPrice={RejectedEntryPrice:F4}; skipping margin and charge recalculation.",
                    quote.TradingSymbol, quote.SymbolToken, q2, rejected.Quantity, entryPrice, rejected.EntryPrice);
                return (0, 0m, 0m, productType);
            }

            if (_rejectedQuantities.TryGetValue(quote.SymbolToken, out rejected))
            {
                _logger.LogInformation(
                    "QUANTITY REJECTION IMPROVED Symbol={Symbol} Token={Token} CurrentMarketQuantity={CurrentQuantity} RejectedQuantity={RejectedQuantity} CurrentEntryPrice={EntryPrice:F4} RejectedEntryPrice={RejectedEntryPrice:F4}; recalculating margin and charges.",
                    quote.TradingSymbol, quote.SymbolToken, q2, rejected.Quantity, entryPrice, rejected.EntryPrice);
            }

            // Only query balance/margin after the rejection gate has
            // established that market quantity or entry price has improved.
            var availableBalance = await _angel.GetAvailableBalanceAsync();
            if (availableBalance <= 0m)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Reason=NoAvailableBalance Balance={Balance:F2}", quote.TradingSymbol, availableBalance);
                return (0, 0m, 0m, productType);
            }
            var q1 = await FindMaximumAffordableQuantityAsync(quote, q2, availableBalance, marginalPrice, productType);
            if (q1.Quantity <= 0) return (0, 0m, 0m, productType);

            var quantity = Math.Min(q1.Quantity, q2);
            if (quantity <= 0)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Reason=NoMarketQuantityAtEntryPrice EntryPrice={EntryPrice:F2} Q1={Q1} Q2={Q2}", quote.TradingSymbol, entryPrice, q1.Quantity, q2);
                return (0, 0m, 0m, productType);
            }

            var margin = quantity == q1.Quantity ? q1.Margin : await _angel.GetRequiredMarginAsync(
                quote.SymbolToken, quantity, marginalPrice, quote.Exchange, productType, "BUY");
            var buyCharges = await _brokerage.CalculateAsync(quote.TradingSymbol, quote.SymbolToken, entryPrice, quantity, BrokerageTransactionType.Buy, quote.Exchange, productType);
            var sellCharges = await _brokerage.CalculateAsync(quote.TradingSymbol, quote.SymbolToken, entryPrice + 0.01m, quantity, BrokerageTransactionType.Sell, quote.Exchange, productType);
            var totalCharges = buyCharges.TotalCharges + sellCharges.TotalCharges;

            _logger.LogInformation("QUANTITY CHECK Symbol={Symbol} ProductType={ProductType} EntryPrice={EntryPrice:F4} Q1Affordable={Q1} Q2Market={Q2} Quantity={Quantity} Balance={Balance:F2} Margin={Margin:F2} BuyCharges={BuyCharges:F2} SellCharges={SellCharges:F2} MoveValue={MoveValue:F2}",
                quote.TradingSymbol, productType, entryPrice, q1.Quantity, q2, quantity, availableBalance, margin, buyCharges.TotalCharges, sellCharges.TotalCharges, quantity * 0.01m);

            if (quantity * 0.01m <= totalCharges)
            {
                _rejectedQuantities[quote.SymbolToken] = new RejectedQuantityState(quantity, entryPrice);
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Reason=ExpectedOnePaisaMoveDoesNotCoverCharges Quantity={Quantity} EntryPrice={EntryPrice:F2} MoveValue={MoveValue:F2} TotalCharges={Charges:F2}; retry only when quantity or entry price decreases.", quote.TradingSymbol, quantity, entryPrice, quantity * 0.01m, totalCharges);
                return (0, 0m, 0m, productType);
            }

            _rejectedQuantities.TryRemove(quote.SymbolToken, out _);
            quote.EstimatedCharges = totalCharges;
            quote.Quantity = quantity;
            quote.TotalInvestment = margin;
            quote.Ltp = entryPrice;
            quote.LowestPriceChange = 0.01m;
            return (quantity, margin, entryPrice, productType);
        }

        private async Task<(int Quantity, decimal Margin)> FindMaximumAffordableQuantityAsync(
            MarketQuoteData quote,
            int maximumMarketQuantity,
            decimal availableBalance,
            decimal marginalPrice,
            string productType)
        {
            if (maximumMarketQuantity <= 0 || availableBalance <= 0m || marginalPrice <= 0m)
                return (0, 0m);

            async Task<decimal?> GetMarginAsync(int quantity)
            {
                try
                {
                    var margin = await _angel.GetRequiredMarginAsync(
                        quote.SymbolToken, quantity, marginalPrice,
                        quote.Exchange, productType, "BUY");

                    _logger.LogInformation(
                        "MARGIN CHECK Symbol={Symbol} ProductType={ProductType} Token={Token} Quantity={Quantity} Price={Price:F4} RequiredMargin={RequiredMargin:G29} AvailableBalance={AvailableBalance:F2}",
                        quote.TradingSymbol, productType, quote.SymbolToken, quantity, marginalPrice, margin, availableBalance);

                    return margin;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "ORDER BLOCKED Symbol={Symbol} Gate=MarginApi Reason=MarginCalculationFailed Quantity={Quantity}; no buy order will be submitted.",
                        quote.TradingSymbol, quantity);
                    return null;
                }
            }

            // Start with available balance expressed as a share quantity, capped by
            // the actual ask quantity available at or below the planned entry price.
            var initialQuantity = (int)Math.Clamp(
                decimal.Floor(availableBalance), 1m, maximumMarketQuantity);

            _logger.LogInformation(
                "AFFORDABLE QUANTITY SEARCH START Symbol={Symbol} ProductType={ProductType} AvailableBalance={Balance:F2} InitialQuantity={InitialQuantity} MaximumMarketQuantity={MaximumMarketQuantity} Price={Price:F4}",
                quote.TradingSymbol, productType, availableBalance, initialQuantity,
                maximumMarketQuantity, marginalPrice);

            var initialMargin = await GetMarginAsync(initialQuantity);
            if (!initialMargin.HasValue)
                return (0, 0m);

            var bestQuantity = initialMargin.Value > 0m && initialMargin.Value <= availableBalance
                ? initialQuantity
                : 0;
            var bestMargin = bestQuantity > 0 ? initialMargin.Value : 0m;

            if (initialQuantity == maximumMarketQuantity)
                return (bestQuantity, bestMargin);

            // If the initial quantity is affordable (or the API returned zero),
            // search upward. A zero margin is never accepted as an affordable result;
            // it is treated only as a low-side search result while probing larger sizes.
            int left;
            int right;
            if (initialMargin.Value <= availableBalance)
            {
                left = initialQuantity + 1;
                right = maximumMarketQuantity;
            }
            else
            {
                // Initial quantity is too expensive: search smaller quantities.
                left = 1;
                right = initialQuantity - 1;
            }

            while (left <= right)
            {
                var quantity = left + (right - left) / 2;
                var margin = await GetMarginAsync(quantity);
                if (!margin.HasValue)
                    return (0, 0m);

                if (margin.Value <= 0m || margin.Value <= availableBalance)
                {
                    if (margin.Value > 0m)
                    {
                        bestQuantity = quantity;
                        bestMargin = margin.Value;
                    }

                    // Zero is not a valid margin result, but continue toward higher
                    // quantities because small positions may be rounded to zero by the API.
                    left = quantity + 1;
                }
                else
                {
                    right = quantity - 1;
                }
            }

            _logger.LogInformation(
                "AFFORDABLE QUANTITY SEARCH COMPLETE Symbol={Symbol} ProductType={ProductType} Quantity={Quantity} Margin={Margin:G29} AvailableBalance={Balance:F2}",
                quote.TradingSymbol, productType, bestQuantity, bestMargin, availableBalance);

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

            // Fixed protective stop: one paisa below the planned entry price.
            return Math.Round(entryPrice - 0.01m, 2, MidpointRounding.AwayFromZero);
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

            if (trade.StopLoss > 0m && currentCandle.Close <= trade.StopLoss)
            {
                outcome = "STOP_LOSS";
                return true;
            }

            return false;
        }

        /// <summary>
        /// Evaluates the profit target against a live WebSocket tick. Unlike the
        /// stop loss, the target is allowed to trigger intraminute.
        /// </summary>
        public async Task ProcessSellTargetTickAsync(TradeOrder trade, decimal ltp, DateTime tickTime)
        {
            if (trade == null || trade.Quantity <= 0 || trade.EntryPrice <= 0m
                || trade.TargetPrice <= 0m || ltp < trade.TargetPrice)
                return;

            _logger.LogInformation(
                "EXIT TARGET TICK TRIGGERED Symbol={Symbol} TickTime={TickTime:yyyy-MM-dd HH:mm:ss.fff} LTP={Ltp:F4} Target={Target:F4}",
                trade.Symbol, tickTime, ltp, trade.TargetPrice);

            var tickCandle = new Candle
            {
                Timestamp = tickTime,
                Open = ltp,
                High = ltp,
                Low = ltp,
                Close = ltp
            };

            await PlaceSellOrder(
                new Dictionary<string, TradeOrder> { [trade.Symbol] = trade },
                new Dictionary<string, List<Candle>> { [trade.SymbolToken] = new() { tickCandle } },
                forceSquareOff: false,
                forcedExitReason: "TARGET");
        }

        /// <summary>
        /// Evaluates a completed one-minute candle for stop-loss only. The profit
        /// target is handled immediately by ProcessSellTargetTickAsync on live ticks.
        /// </summary>
        public async Task ProcessSellCandleAsync(TradeOrder trade, Candle closedCandle)
        {
            if (trade == null || closedCandle == null || closedCandle.Close <= 0m
                || trade.Quantity <= 0 || trade.EntryPrice <= 0m)
                return;

            if (!ValidateSell(trade, closedCandle, out var outcome))
            {
                _logger.LogInformation(
                    "EXIT CANDLE CHECK Symbol={Symbol} CandleTime={CandleTime:yyyy-MM-dd HH:mm:ss} Close={Close:F4} Target={Target:F4} StopLoss={StopLoss:F4} Result=HOLD",
                    trade.Symbol, closedCandle.Timestamp, closedCandle.Close,
                    trade.TargetPrice, trade.StopLoss);
                return;
            }

            _logger.LogInformation(
                "EXIT CANDLE TRIGGERED Symbol={Symbol} CandleTime={CandleTime:yyyy-MM-dd HH:mm:ss} Close={Close:F4} Outcome={Outcome} Target={Target:F4} StopLoss={StopLoss:F4}",
                trade.Symbol, closedCandle.Timestamp, closedCandle.Close,
                outcome, trade.TargetPrice, trade.StopLoss);

            if (!_settings.Current.EnableStopLossExecution)
            {
                EmailHelper.SendEmail(
                    EmailHelper.BuildStopLossAlertEmail(trade, closedCandle, stopLossExecutionEnabled: false),
                    _settings.Current,
                    $"STOP-LOSS ALERT - AUTO-SELL DISABLED - {trade.Symbol} - EntryPrice: {trade.EntryPrice:F2} - StopLoss: {trade.StopLoss:F2}",
                    true);

                _logger.LogWarning(
                    "STOP-LOSS ALERT SENT Symbol={Symbol} CandleTime={CandleTime:yyyy-MM-dd HH:mm:ss} Close={Close:F4} StopLoss={StopLoss:F4} Result=SELL_DISABLED",
                    trade.Symbol, closedCandle.Timestamp, closedCandle.Close, trade.StopLoss);
                return;
            }

            await PlaceSellOrder(
                new Dictionary<string, TradeOrder> { [trade.Symbol] = trade },
                new Dictionary<string, List<Candle>> { [trade.SymbolToken] = new() { closedCandle } },
                forceSquareOff: false,
                forcedExitReason: outcome);
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
                        ProductType = string.IsNullOrWhiteSpace(trade.ProductType) ? "INTRADAY" : trade.ProductType,
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
                    $"{trade.ExecutionMode} CLOSED {trade.Symbol} - Net Profit: {trade.NetProfit:F2}",
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
        

    }
}
