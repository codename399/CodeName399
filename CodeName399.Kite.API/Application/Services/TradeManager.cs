using CodeName399.Kite.API.Repository;
using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Services.Brokerage;
using CodeName399.Kite.API.Services.Interface;
using System.Collections.Concurrent;

namespace CodeName399.Kite.API.Services
{
    public class TradeManager
    {
        private readonly ConcurrentDictionary<string, TradeOrder> _trades = new(
            StringComparer.OrdinalIgnoreCase
        );

        private readonly IAngelOneService _angel;
        private readonly IInstrumentService _instrumentService;
        private readonly ITradeOrderRepository _repository;
        private readonly ILogger<TradeManager> _logger;
        private readonly IBrokerageCalculatorService _brokerage;
        private readonly TradeCloseHistoryStore? _closeHistory;

        private readonly SemaphoreSlim _initializeLock = new(1, 1);
        private bool _initialized;
        private readonly ConcurrentDictionary<string, byte> _lockedSymbols = new(
            StringComparer.OrdinalIgnoreCase
        );

        private readonly TradingSettingsProvider _tradingSettingsProvider;
        private readonly WatchListService _watchList;

        public TradeManager(
            IAngelOneService angel,
            IInstrumentService instrumentService,
            ITradeOrderRepository repository,
            TradingSettingsProvider tradingSettingsProvider,
            WatchListService watchList,
            IBrokerageCalculatorService brokerage,
            ILogger<TradeManager> logger,
            TradeCloseHistoryStore? closeHistory = null
        )
        {
            _angel = angel;
            _instrumentService = instrumentService;
            _repository = repository;
            _tradingSettingsProvider = tradingSettingsProvider;
            _watchList = watchList;
            _brokerage = brokerage;
            _logger = logger;
            _closeHistory = closeHistory;
        }

        public bool Initialized => _initialized;

        private static string TradeKey(TradeOrder trade)
            => !string.IsNullOrWhiteSpace(trade.SymbolToken)
                ? PositionIdentity.Create(trade)
                : trade.Symbol.Trim().ToUpperInvariant();

        private TradeOrder? FindBySymbolOrKey(string symbol)
            => _trades.Values.FirstOrDefault(x => string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyCollection<TradeOrder> GetAll()
        {
            return _trades.Values.ToList();
        }

        public IReadOnlyCollection<TradeOrder> GetOpen()
        {
            return _trades.Values.Where(x => !x.Closed).ToList();
        }

        public IReadOnlyCollection<TradeOrder> GetPending()
        {
            return _trades
                .Values.Where(x => x.Status == TradeStatus.PendingBrokerConfirmation || x.Status == TradeStatus.PartiallyFilled)
                .ToList();
        }

        public bool Contains(string symbol)
        {
            return _trades.Values.Any(x => !x.Closed && x.InstrumentType == InstrumentType.Equity && string.Equals(x.Symbol, symbol.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        public bool Contains(string exchange, string symbolToken) => ContainsPosition(exchange, symbolToken);

        public TradeOrder? Get(string symbol)
        {
            if (_trades.TryGetValue(symbol.Trim().ToUpperInvariant(), out var trade) && trade.InstrumentType == InstrumentType.Equity) return trade;
            var matches = _trades.Values.Where(x => x.InstrumentType == InstrumentType.Equity && string.Equals(x.Symbol, symbol.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            return matches.Count == 1 ? matches[0] : null;
        }

        public bool TryGet(string symbol, out TradeOrder trade)
        {
            trade = Get(symbol)!;
            return trade != null;
        }

        public TradeOrder? GetByPosition(string exchange, string symbolToken)
        {
            if (string.IsNullOrWhiteSpace(symbolToken))
                return null;

            return _trades.Values.FirstOrDefault(x =>
                string.Equals(x.Exchange, exchange, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.SymbolToken, symbolToken, StringComparison.OrdinalIgnoreCase) &&
                !x.Closed);
        }

        public bool ContainsPosition(string exchange, string symbolToken)
            => GetByPosition(exchange, symbolToken) != null;

        public TradeOrder? GetByIdentity(string exchange, string symbolToken, string symbol = "")
        {
            if (!string.IsNullOrWhiteSpace(symbolToken))
                return GetByPosition(exchange, symbolToken);

            var equityMatches = _trades.Values
                .Where(x => !x.Closed
                    && x.InstrumentType == InstrumentType.Equity
                    && string.Equals(x.Exchange, exchange, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(x.Symbol, symbol, StringComparison.OrdinalIgnoreCase))
                .ToList();

            return equityMatches.Count == 1 ? equityMatches[0] : null;
        }

        public void Add(TradeOrder trade)
        {
            if (trade.InstrumentType != InstrumentType.Equity)
                throw new InvalidOperationException($"Only equity trades are supported: {trade.Symbol}.");
            trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();

            trade.Closed = false;

            if (trade.Status == default)
            {
                trade.Status = TradeStatus.PendingBrokerConfirmation;
            }

            if (_trades.TryAdd(TradeKey(trade), trade))
            {
                TryAcquirePosition(trade);
            }
        }

        public void Clear()
        {
            _trades.Clear();
        }

        public async Task InitializeAsync()
        {
            if (_initialized)
                return;

            await _initializeLock.WaitAsync();

            try
            {
                if (_initialized)
                    return;

                await RecoverAsync();

                _initialized = true;
            }
            finally
            {
                _initializeLock.Release();
            }
        }

        private async Task RecoverAsync()
        {
            _trades.Clear();

            //----------------------------------------------------------
            // 1. Recover active trades from per-trade recovery files (Source of Truth)
            //----------------------------------------------------------

            var trades = await _repository.GetOpenTradesAsync();

            foreach (var trade in trades)
            {
                trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();
                NormalizeTradeInstrument(trade);

                _trades[TradeKey(trade)] = trade;

                TryAcquirePosition(trade);
            }

            //----------------------------------------------------------
            // Paper Trading
            //----------------------------------------------------------

            var hasPersistedLiveTrades = trades.Any(x => x.ExecutionMode == TradeExecutionMode.Live && !x.Closed);
            if (_tradingSettingsProvider.Current.PaperTrading && !hasPersistedLiveTrades)
            {
                _logger.LogInformation("Paper Trading enabled and no persisted live trades exist. Skipping broker position recovery.");
                return;
            }

            //----------------------------------------------------------
            // 2. Recover any missing trades from broker positions
            //----------------------------------------------------------

            var positions = await _angel.GetPositionsAsync(true);

            if (positions.Count == 0)
                return;

            var orders = await _angel.GetOrderBookAsync();
            var instrumentMap = (await _instrumentService.LoadInstruments())
                .Where(x => !string.IsNullOrWhiteSpace(x.token))
                .GroupBy(x => $"{x.exch_seg}|{x.token}", StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            foreach (var position in positions)
            {
                if (position.NetQty <= 0)
                {
                    _logger.LogWarning("Ignoring unsupported short broker position {Symbol}. Equity recovery is long-only.", position.TradingSymbol);
                    continue;
                }

                string symbol = position.TradingSymbol.Trim().ToUpperInvariant();

                //------------------------------------------------------
                // Already recovered from recovery files
                //------------------------------------------------------

                if (GetByPosition(position.Exchange, position.SymbolToken) != null)
                    continue;

                const string entryTransaction = "BUY";
                var buyOrder = orders
                    .Where(x => x.TransactionType.Equals(entryTransaction, StringComparison.OrdinalIgnoreCase))
                    .Where(x =>
                        x.OrderStatus.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase)
                    )
                    .Where(x => string.IsNullOrWhiteSpace(x.SymbolToken) || string.Equals(x.SymbolToken, position.SymbolToken, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.IsNullOrWhiteSpace(x.Exchange) || string.Equals(x.Exchange, position.Exchange, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(x => x.UpdateTime)
                    .FirstOrDefault();

                if (buyOrder == null)
                    continue;

                decimal brokerEntryPrice = position.NetQty < 0 ? position.SellAvgPrice : position.BuyAvgPrice;
                decimal entryPrice =
                    buyOrder.AveragePrice > 0 ? buyOrder.AveragePrice : brokerEntryPrice;

                var watch = _watchList.Get(position.SymbolToken); var trade = new TradeOrder
                {
                    Symbol = symbol,
                    SymbolToken = position.SymbolToken,
                    Exchange = string.IsNullOrWhiteSpace(position.Exchange) ? "NSE" : position.Exchange,
                    ProductType = string.IsNullOrWhiteSpace(position.ProductType) ? "INTRADAY" : position.ProductType,
                    InstrumentType = InstrumentType.Equity,
                    Direction = TradeDirection.Long,
                    ExecutionMode = TradeExecutionMode.Live,

                    Quantity = Math.Abs(position.NetQty),

                    EntryPrice = entryPrice,

                    EntryTime = DateTime.TryParse(buyOrder.UpdateTime, out var dt)
                        ? dt
                        : DateTime.UtcNow,

                    HighestPrice = entryPrice,
                    LowestPrice = entryPrice,
                    CurrentPrice = position.Ltp > 0 ? position.Ltp : entryPrice,

                    BuyOrderId = buyOrder.OrderId,

                    StopLoss = watch?.StopLoss ?? 0m,
                    InitialStopLoss = watch?.StopLoss ?? 0m,


                    InitialRiskPerShare =
                        watch != null
                            ? (position.NetQty < 0
                                ? Math.Max(0m, watch.StopLoss - entryPrice)
                                : Math.Max(0m, entryPrice - watch.StopLoss))
                            : 0m,

                    TrailingActivated = false,

                    Status = TradeStatus.Open,
                    Closed = false,
                    EntryTransactionType = entryTransaction,
                };

                IndicatorMapper.ApplyTradeIndicators(trade, watch);

                if (watch != null)
                {

                    if (string.IsNullOrWhiteSpace(trade.EntryReason))
                        trade.EntryReason = watch.Reason;
                    if (string.IsNullOrWhiteSpace(trade.EntryDecision))
                        trade.EntryDecision = "RECOVERED";
                    trade.InitialRiskPerShare = trade.Direction == TradeDirection.Short
                        ? Math.Max(0m, trade.InitialStopLoss - trade.EntryPrice)
                        : Math.Max(0m, trade.EntryPrice - trade.InitialStopLoss);

                    trade.EntryIndicators = IndicatorMapper.ToTradeIndicatorSnapshot(watch);
                }

                _trades[TradeKey(trade)] = trade;

                TryAcquirePosition(trade);
                await SaveAsync(trade);

                _logger.LogWarning(
                    "Recovered missing trade {Symbol} from broker because it was not found in recovery files.",
                    trade.Symbol
                );
            }
        }

        private static void NormalizeTradeInstrument(TradeOrder trade)
        {
            trade.InstrumentType = InstrumentType.Equity;
            if (string.IsNullOrWhiteSpace(trade.Exchange))
                trade.Exchange = "NSE";
            if (string.IsNullOrWhiteSpace(trade.ProductType))
                trade.ProductType = "INTRADAY";
            if (string.IsNullOrWhiteSpace(trade.OrderType))
                trade.OrderType = "MARKET";
            if (string.IsNullOrWhiteSpace(trade.Duration))
                trade.Duration = "DAY";
            trade.EntryTransactionType = "BUY";
        }

        public async Task SaveAsync(TradeOrder trade)
        {
            ArgumentNullException.ThrowIfNull(trade);
            using var flow = TradingFlowActivity.Start(
                "trading.trade-save", trade.Symbol, trade.SymbolToken,
                trade.InstrumentType.ToString(), trade.Direction.ToString());
            _logger.LogDebug("FLOW STAGE Trade.Save Start Symbol={Symbol}, Token={Token}, TradeId={TradeId}, Status={Status}, ExecutionMode={ExecutionMode}, Quantity={Quantity}, EntryPrice={EntryPrice}",
                trade.Symbol, trade.SymbolToken, trade.Id, trade.Status, trade.ExecutionMode, trade.Quantity, trade.EntryPrice);

            trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();

            _trades[TradeKey(trade)] = trade;

            try
            {
                await _repository.SaveAsync(trade);
                _logger.LogDebug("FLOW STAGE Trade.Save Complete Symbol={Symbol}, Token={Token}, TradeId={TradeId}",
                    trade.Symbol, trade.SymbolToken, trade.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist trade {TradeId} for {Symbol}. It will be recovered by BrokerReconciliationService.",
                    trade.Id,
                    trade.Symbol
                );
            }
        }

        // Synchronous callers update the in-memory trade immediately and schedule
        // the existing asynchronous persistence path. Async callers should use
        // UpdateAsync directly.
        private void Update(TradeOrder trade)
        {
            ArgumentNullException.ThrowIfNull(trade);
            _trades[TradeKey(trade)] = trade;
            _ = UpdateAsync(trade);
        }

        public async Task UpdateAsync(TradeOrder trade)
        {
            ArgumentNullException.ThrowIfNull(trade);
            using var flow = TradingFlowActivity.Start(
                "trading.trade-update", trade.Symbol, trade.SymbolToken,
                trade.InstrumentType.ToString(), trade.Direction.ToString());
            _logger.LogDebug(
                "FLOW STAGE Trade.Update Start Symbol={Symbol}, Token={Token}, TradeId={TradeId}, Status={Status}, Closed={Closed}, ExitReason={ExitReason}",
                trade.Symbol, trade.SymbolToken, trade.Id, trade.Status, trade.Closed, trade.ExitReason);

            trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();

            _trades[TradeKey(trade)] = trade;

            try
            {
                await _repository.UpdateAsync(trade);
                _logger.LogDebug(
                    "FLOW STAGE Trade.Update Complete Symbol={Symbol}, Token={Token}, TradeId={TradeId}",
                    trade.Symbol, trade.SymbolToken, trade.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to persist update for trade {TradeId} ({Symbol}). It will be recovered by BrokerReconciliationService.",
                    trade.Id,
                    trade.Symbol
                );
            }
        }

        public Dictionary<string, TradeOrder> GetDictionary()
        {
            return _trades.ToDictionary(x => x.Key, x => x.Value, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsOpen(string symbol)
            => _trades.Values.Any(x => !x.Closed
                && x.InstrumentType == InstrumentType.Equity
                && string.Equals(x.Symbol, symbol.Trim(), StringComparison.OrdinalIgnoreCase));

        public TradeOrder? GetByIdentityOrThrow(string exchange, string symbolToken)
            => GetByPosition(exchange, symbolToken);

        public bool IsOpen(string exchange, string symbolToken)
            => GetByPosition(exchange, symbolToken) != null;

        public void MarkExitPending(TradeOrder trade)
        {
            trade.ExitOrderPlaced = true;
            trade.ExitOrderPlacedAt = DateTime.UtcNow;
            trade.ExitLastAttemptTime = DateTime.UtcNow;
            trade.ExitRetryCount++;

            Update(trade);
        }

        public void ResetExitPending(TradeOrder trade)
        {
            trade.ExitOrderPlaced = false;
            trade.ExitOrderPlacedAt = null;
            trade.ExitLastAttemptTime = null;
            trade.ExitOrderId = string.Empty;
            trade.ExitUniqueOrderId = string.Empty;

            Update(trade);
        }

        public void UpdateTrailing(
            TradeOrder trade,
            decimal extremePrice,
            decimal stopLoss
        )
        {
            if (trade.Direction == TradeDirection.Short)
                trade.LowestPrice = Math.Min(trade.LowestPrice <= 0 ? extremePrice : trade.LowestPrice, extremePrice);
            else
                trade.HighestPrice = Math.Max(trade.HighestPrice, extremePrice);

            trade.StopLoss = stopLoss;
            trade.TrailingMoveCount++;

            trade.TrailingProtectedProfit = trade.Direction == TradeDirection.Short
                ? Math.Max(0, trade.EntryPrice - stopLoss)
                : Math.Max(0, stopLoss - trade.EntryPrice);

            // Activation is owned exclusively by SellTradingService after
            // this stock's own executable-side net profit is positive.
            // UpdateTrailing only ratchets an already-active trailing stop.
            if (!trade.TrailingActivated)
                return;

            Update(trade);
        }

        public async Task MarkRejectedAsync(TradeOrder trade, string? reason = null)
        {
            trade.Status = TradeStatus.Rejected;
            trade.Closed = true;
            trade.ExitReason = reason;

            await UpdateAsync(trade);

            ReleasePosition(trade);
            ReleaseSymbol(trade.Symbol);

            _logger.LogWarning("{Symbol} rejected {Reason}", trade.Symbol, reason);
        }

        public void UpdateExecution(
            TradeOrder trade,
            int quantity,
            decimal entryPrice,
            decimal stopLoss,
            decimal breakEvenPrice,
            decimal minimumExitPrice,
            string? orderId,
            string? uniqueOrderId,
            decimal estimatedCharges = 0,
            decimal estimatedGrossProfit = 0,
            decimal estimatedNetProfit = 0,
            decimal chargesPercent = 0
        )
        {
            trade.Status = TradeStatus.Open;
            trade.Closed = false;
            trade.ExitOrderPlaced = false;
            trade.EntryFilledQuantity = quantity;
            trade.EntryRequestedQuantity = Math.Max(trade.EntryRequestedQuantity, quantity);
            trade.EntryAveragePrice = entryPrice;
            trade.EntryExecutionState = OrderExecutionState.Complete;
            trade.EntryLastUpdateTime = DateTime.UtcNow;

            trade.Quantity = quantity;
            trade.EntryPrice = entryPrice;

            //------------------------------------------------------
            // Preserve original setup
            //------------------------------------------------------

            if (trade.InitialStopLoss <= 0)
                trade.InitialStopLoss = stopLoss;

            if (trade.InitialRiskPerShare <= 0)
                trade.InitialRiskPerShare = trade.Direction == TradeDirection.Short
                    ? stopLoss - entryPrice
                    : entryPrice - stopLoss;

            trade.StopLoss = stopLoss;

            trade.HighestPrice = Math.Max(trade.HighestPrice, entryPrice);

            if (trade.LowestPrice <= 0)
                trade.LowestPrice = entryPrice;

            decimal calculatedBreakEven;

            if (breakEvenPrice > 0)
            {
                calculatedBreakEven = breakEvenPrice;
            }
            else
            {
                decimal chargesPerShare = quantity > 0 ? estimatedCharges / quantity : 0m;

                calculatedBreakEven = trade.Direction == TradeDirection.Short
                    ? OrderPriceValidator.RoundDown(entryPrice - chargesPerShare)
                    : OrderPriceValidator.RoundUp(entryPrice + chargesPerShare);
            }

            trade.BreakEvenPrice = calculatedBreakEven;

            trade.MinimumExitPrice = minimumExitPrice > 0 ? minimumExitPrice : calculatedBreakEven;

            if (!string.IsNullOrWhiteSpace(orderId))
                trade.BuyOrderId = orderId;

            if (!string.IsNullOrWhiteSpace(uniqueOrderId))
                trade.UniqueOrderId = uniqueOrderId;

            trade.EstimatedCharges = estimatedCharges;
            trade.EstimatedGrossProfit = estimatedGrossProfit;
            trade.EstimatedNetProfit = estimatedNetProfit;
            trade.ChargesPercent = chargesPercent;

            Update(trade);
        }

        public bool ConfirmBuy(
            string symbol,
            string orderId,
            string? uniqueOrderId,
            int quantity,
            decimal averagePrice,
            decimal stopLoss,
            decimal breakEven,
            decimal minimumExitPrice,
            decimal estimatedCharges,
            decimal estimatedGrossProfit,
            decimal estimatedNetProfit,
            decimal chargesPercent,
            out TradeOrder? trade
        )
        {
            trade = null;

            var existing = Get(symbol);
            if (existing == null)
                return false;

            if (existing.Status == TradeStatus.Open)
                return false;

            if (existing.Closed)
                return false;

            UpdateExecution(
                existing,
                quantity,
                averagePrice,
                stopLoss,
                breakEven,
                minimumExitPrice,
                orderId,
                uniqueOrderId,
                estimatedCharges,
                estimatedGrossProfit,
                estimatedNetProfit,
                chargesPercent
            );

            trade = existing;

            return true;
        }

        public async Task<bool> ConfirmSell(
            string symbol,
            decimal exitPrice,
            DateTime exitTime,
            string exitOrderId,
            int filledQuantity = 0
        )
        {
            var trade = Get(symbol);
            if (trade == null)
                return false;

            if (trade.Closed)
                return false;

            var actualFilledQuantity = filledQuantity > 0 ? filledQuantity : trade.Quantity;
            if (actualFilledQuantity < trade.Quantity)
            {
                trade.ExitFilledQuantity = actualFilledQuantity;
                trade.ExitAveragePrice = exitPrice;
                trade.ExitExecutionState = OrderExecutionState.PartiallyFilled;
                trade.ExitLastUpdateTime = exitTime;
                await UpdateAsync(trade);
                return false;
            }

            trade.ExitFilledQuantity = actualFilledQuantity;
            trade.ExitAveragePrice = exitPrice;
            trade.ExitExecutionState = OrderExecutionState.Complete;
            trade.ExitLastUpdateTime = exitTime;
            trade.ExitPrice = exitPrice;

            trade.ExitTime = exitTime;

            var latest = _watchList.Get(trade.SymbolToken);

            if (latest != null)
            {
                trade.ExitIndicators = IndicatorMapper.ToTradeIndicatorSnapshot(latest);
            }

            trade.ExitOrderId = exitOrderId;

            //------------------------------------------------------
            await CloseAsync(trade);

            return true;
        }

        public TradeOrder? FindPending(string symbol)
        {
            var trade = Get(symbol);
            if (trade == null)
                return null;

            return trade.Status == TradeStatus.PendingBrokerConfirmation
                || trade.Status == TradeStatus.PartiallyFilled
                ? trade
                : null;
        }

        public TradeOrder? FindPending(string exchange, string symbolToken, string symbol = "")
        {
            var trade = GetByPosition(exchange, symbolToken);
            if (trade != null && (trade.Status == TradeStatus.PendingBrokerConfirmation || trade.Status == TradeStatus.PartiallyFilled))
                return trade;
            return !string.IsNullOrWhiteSpace(symbol) ? FindPending(symbol) : null;
        }

        public async Task UpdatePartialEntryAsync(
            TradeOrder trade,
            string orderId,
            int requestedQuantity,
            int filledQuantity,
            decimal averagePrice,
            OrderExecutionState executionState,
            DateTime updatedTime)
        {
            trade.Status = TradeStatus.PartiallyFilled;
            trade.Closed = false;
            trade.EntryRequestedQuantity = Math.Max(trade.EntryRequestedQuantity, requestedQuantity);
            trade.EntryFilledQuantity = Math.Max(trade.EntryFilledQuantity, filledQuantity);
            trade.EntryAveragePrice = averagePrice > 0 ? averagePrice : trade.EntryAveragePrice;
            trade.EntryExecutionState = executionState;
            trade.EntryLastUpdateTime = updatedTime;
            if (!string.IsNullOrWhiteSpace(orderId))
                trade.BuyOrderId = orderId;

            await UpdateAsync(trade);
        }

        public async Task UpdatePartialExitAsync(
            TradeOrder trade,
            string orderId,
            int requestedQuantity,
            int filledQuantity,
            decimal averagePrice,
            OrderExecutionState executionState,
            DateTime updatedTime)
        {
            trade.ExitRequestedQuantity = Math.Max(trade.ExitRequestedQuantity, requestedQuantity);
            trade.ExitFilledQuantity = Math.Max(trade.ExitFilledQuantity, filledQuantity);
            trade.ExitAveragePrice = averagePrice > 0 ? averagePrice : trade.ExitAveragePrice;
            trade.ExitExecutionState = executionState;
            trade.ExitLastUpdateTime = updatedTime;
            if (!string.IsNullOrWhiteSpace(orderId))
                trade.ExitOrderId = orderId;

            await UpdateAsync(trade);
        }

        public bool Remove(string symbol)
        {
            symbol = symbol.Trim().ToUpperInvariant();
            ReleaseSymbol(symbol);
            var trade = Get(symbol);
            return trade != null && _trades.TryRemove(TradeKey(trade), out _);
        }

        public bool IsPending(string symbol)
        {
            var trade = Get(symbol);
            return trade != null && trade.Status == TradeStatus.PendingBrokerConfirmation;
        }

        public async Task CloseAsync(TradeOrder trade)
        {
            trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();
            using var flow = TradingFlowActivity.Start(
                "trading.trade-close", trade.Symbol, trade.SymbolToken,
                trade.InstrumentType.ToString(), trade.Direction.ToString());
            _logger.LogDebug("FLOW START Trade.Close Symbol={Symbol}, Token={Token}, TradeId={TradeId}, Entry={Entry}, Exit={Exit}, Reason={Reason}",
                trade.Symbol, trade.SymbolToken, trade.Id, trade.EntryPrice, trade.ExitPrice, trade.ExitReason);

            trade.Status = TradeStatus.Closed;

            trade.Closed = true;

            trade.ExitTime ??= DateTime.UtcNow;

            if (string.IsNullOrWhiteSpace(trade.ExitCategory))
            {
                trade.ExitCategory = trade.ExitReason switch
                {
                    "Market Close" or "Intraday square-off" => "SESSION",
                    "Stop Loss" => "RISK",
                    _ when trade.ExitReason.Contains("Trailing", StringComparison.OrdinalIgnoreCase) => "PROFIT_PROTECTION",
                    _ => "MANUAL"
                };
            }
            if (string.IsNullOrWhiteSpace(trade.ExitTrigger))
            {
                trade.ExitTrigger = trade.ExitReason switch
                {
                    "Market Close" or "Intraday square-off" => "MARKET_CLOSE",
                    "Stop Loss" => "STOP_LOSS",
                    _ when trade.ExitReason.Contains("Trailing", StringComparison.OrdinalIgnoreCase) => "TRAILING_STOP",
                    _ => "MANUAL"
                };
            }
            if (trade.ExitTriggerPrice <= 0)
                trade.ExitTriggerPrice = trade.ExitPrice;

            await CalculateRealizedPnlAsync(trade);
            trade.Research.NetProfitAtExit = trade.RealizedNetProfit;
            trade.Research.GrossProfitAtExit = trade.RealizedGrossProfit;
            trade.Research.RealizedChargesAtExit = trade.RealizedCharges;
            trade.Research.WasNetProfitableAtExit = trade.RealizedNetProfit > 0m;
            trade.Research.PositiveNetProfitObserved |= trade.RealizedNetProfit > 0m;
            trade.Research.HoldingSeconds = Math.Max(0, ((trade.ExitTime ?? DateTime.UtcNow) - trade.EntryTime).TotalSeconds);
            if (trade.Research.LastWasPositive && trade.Research.LastPositiveStateTime.HasValue)
                trade.Research.TimeInPositiveNetProfitSeconds += Math.Max(0, ((trade.ExitTime ?? DateTime.UtcNow) - trade.Research.LastPositiveStateTime.Value).TotalSeconds);
            if (trade.RealizedNetProfit > trade.Research.PeakNetProfit)
            {
                trade.Research.PeakNetProfit = trade.RealizedNetProfit;
                trade.Research.PeakNetProfitTime = trade.ExitTime ?? DateTime.UtcNow;
            }
            trade.EntryCategoryVersion = string.IsNullOrWhiteSpace(trade.EntryCategoryVersion) ? _tradingSettingsProvider.Current.ResearchDataVersion : trade.EntryCategoryVersion;
            trade.ExitCategoryVersion = string.IsNullOrWhiteSpace(trade.ExitCategoryVersion) ? _tradingSettingsProvider.Current.ResearchDataVersion : trade.ExitCategoryVersion;
            try
            {
                if (trade.ExitIndicators == null)
                {
                    var latest = _watchList.Get(trade.SymbolToken);

                    if (latest != null)
                    {
                        trade.ExitIndicators = IndicatorMapper.ToTradeIndicatorSnapshot(latest);
                    }
                }

                if (_closeHistory != null)
                    await _closeHistory.AppendAsync(trade);

                // Closed trades are never retained in the recovery store.
                await _repository.DeleteAsync(trade.Id);
                _trades.TryRemove(TradeKey(trade), out _);

                ReleasePosition(trade);
                ReleaseSymbol(trade.Symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed closing trade {Symbol}.", trade.Symbol);

                throw;
            }
        }

        public bool RefreshTrade(string symbol, Action<TradeOrder> updater)
        {
            var trade = FindBySymbolOrKey(symbol);
            if (trade == null) return false;
            updater(trade);
            Update(trade);
            return true;
        }

        public bool RefreshTrade(string exchange, string symbolToken, Action<TradeOrder> updater)
        {
            var trade = GetByPosition(exchange, symbolToken);
            if (trade == null) return false;
            updater(trade);
            Update(trade);
            return true;
        }

        public TradeOrder GetOrAdd(TradeOrder trade)
        {
            if (trade.InstrumentType != InstrumentType.Equity)
                throw new InvalidOperationException($"Only equity trades are supported: {trade.Symbol}.");
            trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();
            trade.EntryTransactionType = trade.Direction == TradeDirection.Short ? "SELL" : "BUY";
            trade.ExitTransactionType = trade.Direction == TradeDirection.Short ? "BUY" : "SELL";

            var result = _trades.GetOrAdd(
                TradeKey(trade),
                _ =>
                {
                    trade.Status =
                        trade.Status == default
                            ? TradeStatus.PendingBrokerConfirmation
                            : trade.Status;

                    trade.Closed = false;

                    return trade;
                }
            );

            TryAcquirePosition(result);
            if (result.InstrumentType == InstrumentType.Equity)
                TryAcquireSymbol(result.Symbol);

            return result;
        }

        public bool TryAcquireSymbol(string symbol)
            => _lockedSymbols.TryAdd(symbol.Trim().ToUpperInvariant(), 0);

        public bool TryAcquirePosition(TradeOrder trade)
            => _lockedSymbols.TryAdd(TradeKey(trade), 0);

        public void ReleaseSymbol(string symbol)
        {
            if (!string.IsNullOrWhiteSpace(symbol))
                _lockedSymbols.TryRemove(symbol.Trim().ToUpperInvariant(), out _);
        }

        public void ReleasePosition(TradeOrder trade)
            => _lockedSymbols.TryRemove(TradeKey(trade), out _);

        public bool IsSymbolLocked(string symbol)
            => _lockedSymbols.ContainsKey(symbol.Trim().ToUpperInvariant());

        public bool IsPositionLocked(string exchange, string symbolToken)
            => !string.IsNullOrWhiteSpace(symbolToken) && _lockedSymbols.ContainsKey(PositionIdentity.Create(exchange, symbolToken));


        public bool TryCreatePendingTrade(TradeOrder trade, out TradeOrder? existingTrade)
        {
            existingTrade = null;
            if (trade.InstrumentType != InstrumentType.Equity)
                throw new InvalidOperationException($"Only equity trades are supported: {trade.Symbol}.");

            trade.Symbol = trade.Symbol.Trim().ToUpperInvariant();
            trade.EntryTransactionType = trade.Direction == TradeDirection.Short ? "SELL" : "BUY";
            trade.ExitTransactionType = trade.Direction == TradeDirection.Short ? "BUY" : "SELL";

            var acquired = TryAcquirePosition(trade);
            if (trade.InstrumentType == InstrumentType.Equity && !acquired)
                acquired = TryAcquireSymbol(trade.Symbol);
            if (!acquired)
            {
                TryGet(trade.Symbol, out existingTrade!);
                return false;
            }

            trade.Status = TradeStatus.PendingBrokerConfirmation;
            trade.Closed = false;
            trade.EntryRequestedQuantity = trade.Quantity;
            trade.EntryFilledQuantity = 0;
            trade.EntryExecutionState = OrderExecutionState.Submitted;

            if (!_trades.TryAdd(TradeKey(trade), trade))
            {
                ReleasePosition(trade);
                ReleaseSymbol(trade.Symbol);
                TryGet(trade.Symbol, out existingTrade!);
                return false;
            }

            existingTrade = trade;

            return true;
        }

        public bool UsesTrailingConfirmation(TradeOrder trade)
        {
            return trade.Direction == TradeDirection.Short
                ? trade.LowestPrice < trade.EntryPrice && trade.StopLoss <= trade.EntryPrice
                : trade.HighestPrice > trade.EntryPrice && trade.StopLoss >= trade.EntryPrice;
        }

        public bool ShouldConfirmTrailingExit(TradeOrder trade, decimal currentPrice)
        {
            bool breached = trade.Direction == TradeDirection.Short
                ? currentPrice >= trade.StopLoss
                : currentPrice <= trade.StopLoss;

            bool recovered = trade.Direction == TradeDirection.Short
                ? currentPrice < trade.StopLoss
                : currentPrice > trade.StopLoss;

            if (!UsesTrailingConfirmation(trade))
                return breached;

            if (!trade.StopLossTriggered)
            {
                if (breached)
                {
                    trade.StopLossTriggered = true;
                    trade.StopLossTriggerPrice = currentPrice;
                    trade.StopLossTriggerTime = DateTime.UtcNow;
                    Update(trade);
                }

                return false;
            }

            if (recovered)
            {
                trade.StopLossTriggered = false;
                trade.StopLossTriggerPrice = 0;
                trade.StopLossTriggerTime = null;
                Update(trade);
                return false;
            }

            if (trade.StopLossTriggerTime.HasValue
                && DateTime.UtcNow - trade.StopLossTriggerTime.Value >= TimeSpan.FromSeconds(Math.Max(0, _tradingSettingsProvider.Current.StopLossConfirmationSeconds)))
            {
                trade.StopLossTriggered = false;
                trade.StopLossTriggerPrice = 0;
                trade.StopLossTriggerTime = null;
                Update(trade);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Calculates current estimated round-trip net profit using the charges
        /// captured during trade validation.
        /// </summary>
        private async Task CalculateRealizedPnlAsync(TradeOrder trade)
        {
            if (trade.EntryPrice <= 0m || trade.ExitPrice <= 0m || trade.Quantity <= 0)
                return;

            trade.RealizedGrossProfit = TradePnlCalculator.Gross(trade, trade.ExitPrice);

            try
            {
                var entryTransaction = trade.Direction == TradeDirection.Short
                    ? BrokerageTransactionType.Sell
                    : BrokerageTransactionType.Buy;
                var exitTransaction = trade.Direction == TradeDirection.Short
                    ? BrokerageTransactionType.Buy
                    : BrokerageTransactionType.Sell;

                var entryCharges = await _brokerage.CalculateAsync(
                    trade.Symbol, trade.SymbolToken, trade.EntryPrice, trade.Quantity,
                    entryTransaction, trade.Exchange, trade.ProductType);
                var exitCharges = await _brokerage.CalculateAsync(
                    trade.Symbol, trade.SymbolToken, trade.ExitPrice, trade.Quantity,
                    exitTransaction, trade.Exchange, trade.ProductType);

                trade.RealizedCharges = BrokerageCharge.Combine(entryCharges, exitCharges).TotalCharges;
            }
            catch (Exception ex)
            {
                // Closing a filled trade must not fail because the brokerage estimator is unavailable.
                // Fall back to the entry-time round-trip estimate and keep the fallback visible in logs.
                trade.RealizedCharges = Math.Max(0m, trade.EstimatedCharges);
                _logger.LogWarning(ex, "Unable to calculate realized brokerage for {Symbol}; using estimated charges {Charges:F2}.", trade.Symbol, trade.RealizedCharges);
            }

            trade.RealizedNetProfit = TradePnlCalculator.Net(trade, trade.ExitPrice, trade.RealizedCharges);
            var investment = trade.EntryPrice * trade.Quantity;
            trade.RealizedRoiPercent = investment > 0m
                ? Math.Round(trade.RealizedNetProfit / investment * 100m, 4)
                : 0m;

            // Keep the live estimate separate from the immutable realized result.
            trade.EstimatedNetProfit = trade.RealizedNetProfit;
        }

        public decimal CalculateEstimatedNetProfit(TradeOrder trade, decimal exitPrice)
        {
            if (trade.Quantity <= 0 || trade.EntryPrice <= 0 || exitPrice <= 0)
                return 0m;

            decimal grossProfit = TradePnlCalculator.Gross(trade, exitPrice);

            // Net P&L uses the actual executable exit price and estimated charges.
            var charges = Math.Max(0m, trade.EstimatedEntryCharges)
                        + Math.Max(0m, trade.EstimatedExitCharges);
            if (charges <= 0m)
                charges = Math.Max(0m, trade.EstimatedCharges);

            return grossProfit - charges;
        }


        public IReadOnlyCollection<TradeOrder> GetOpenTrades()
        {
            return _trades.Values.ToList().AsReadOnly();
        }
    }
}
