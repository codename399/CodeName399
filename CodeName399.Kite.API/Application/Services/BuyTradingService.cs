using System.Collections.Concurrent;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Helpers.Calculators;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.Models.WebSocketOrder;
using CodeName399.Kite.API.Services.Brokerage;
using CodeName399.Kite.API.Services.Interface;
using CodeName399.Kite.API.WebSocket.Interface;

namespace CodeName399.Kite.API.Services
{
    public class BuyTradingService : TradingServiceBase
    {
        private readonly IIndicatorService _indicatorService;
        private readonly IntradayRiskGuard _intradayRiskGuard;
        private readonly PaperPortfolioService _paperPortfolio;
        private readonly MarketClock _marketClock;
        private readonly EmaCrossoverStrategyService _emaStrategy;
        private readonly IHistoricalDataService _historicalDataService;
        private decimal _cachedBalance;
        private DateTime _lastBalanceRefresh = DateTime.MinValue;
        private readonly ConcurrentDictionary<string, string> _pendingBuyEmailDetails = new();

        public BuyTradingService(
            IAngelOneService angel,
            TradingSettingsProvider tradingSettingsProvider,
            TradingStateService state,
            TradeManager tradeManager,
            ITradeOrderFileRepository tradeHistoryRepository,
            IBrokerageCalculatorService brokerage,
            OrderStatusCache orderCache,
            IAngelOrderWebSocket orderWebSocket,
            TradeValidationService tradeValidation,
            WatchListService watchList,
            SymbolLockService symbolLocks,
            IIndicatorService indicatorService,
            IntradayRiskGuard intradayRiskGuard,
            PaperPortfolioService paperPortfolio,
            MarketClock marketClock,
            EmaCrossoverStrategyService emaStrategy,
            IHistoricalDataService historicalDataService,
            ILogger<BuyTradingService> logger
        )
            : base(
                angel,
                tradingSettingsProvider,
                state,
                tradeManager,
                tradeHistoryRepository,
                brokerage,
                orderCache,
                orderWebSocket,
                tradeValidation,
                watchList,
                symbolLocks,
                logger
            )
        {
            _orderWebSocket.OrderUpdated += OnOrderUpdated;
            _indicatorService = indicatorService;
            _intradayRiskGuard = intradayRiskGuard;
            _paperPortfolio = paperPortfolio;
            _marketClock = marketClock;
            _emaStrategy = emaStrategy;
            _historicalDataService = historicalDataService;
        }

        public async Task Execute()
        {
            var settings = _tradingSettingsProvider.Current;
            var gainers = _watchList.GetBuyCandidates().ToList();
            _logger.LogInformation(
                "ORDER DIAGNOSTIC BuyCycleStarted CandidateCount={CandidateCount} AutoTrading={AutoTrading} PaperTrading={PaperTrading} KillSwitch={KillSwitch} IgnoreMarketHours={IgnoreMarketHours} EntryWindowOpen={EntryWindowOpen} WatchlistCount={WatchlistCount} SubscribedCount={SubscribedCount} OpenTrades={OpenTrades}",
                gainers.Count, settings.EnableAutoTrading, settings.PaperTrading, _state.KillSwitch,
                settings.IgnoreMarketHours, _marketClock.IsNewIntradayEntryWindowOpen,
                _watchList.Current.Count, _watchList.Current.Count(x => x.IsSubscribed), _tradeManager.GetOpenTrades().Count());

            if (!CanExecute())
            {
                _logger.LogWarning("ORDER BLOCKED Gate=CanExecute Reason=Trading disabled and paper trading disabled, or kill switch active. AutoTrading={AutoTrading} PaperTrading={PaperTrading} KillSwitch={KillSwitch}", settings.EnableAutoTrading, settings.PaperTrading, _state.KillSwitch);
                return;
            }

            if (!_state.CanTrade(settings))
            {
                _logger.LogWarning("ORDER BLOCKED Gate=TradingState Reason=CanTrade returned false. KillSwitch={KillSwitch}", _state.KillSwitch);
                return;
            }

            // All entries obey the broker session and intraday cutoff.
            if (!_marketClock.IsNewIntradayEntryWindowOpen)
            {
                _logger.LogInformation("ORDER BLOCKED Gate=MarketHours Reason=New-entry window closed. MarketTime={MarketTime} MarketOpen={MarketOpen} MarketClose={MarketClose} EntryCutoff={EntryCutoff}", _marketClock.Now, settings.MarketOpenTime, settings.MarketCloseTime, _marketClock.GetEntryCutoffTime(settings));
                return;
            }

            if (gainers.Count == 0)
                _logger.LogWarning("ORDER BLOCKED Gate=Candidates Reason=WatchListService.GetBuyCandidates returned zero candidates. WatchlistCount={WatchlistCount} SubscribedCount={SubscribedCount}", _watchList.Current.Count, _watchList.Current.Count(x => x.IsSubscribed));

            string jwtToken;
            try
            {
                jwtToken = (await _angel.GetLoginDataAsync()).JwtToken;
                _logger.LogDebug("ORDER DIAGNOSTIC BrokerSessionReady JwtTokenPresent={JwtTokenPresent}", !string.IsNullOrWhiteSpace(jwtToken));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ORDER BLOCKED Gate=BrokerLogin Reason=Unable to obtain broker login data.");
                return;
            }

            try
            {
                await RefreshSubscribedStocksAsync(jwtToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ORDER DIAGNOSTIC IndicatorRefreshFailed; continuing with current watchlist data.");
            }

            decimal remainingBalance;
            try
            {
                remainingBalance = await GetAvailableBalanceAsync();
                _logger.LogInformation("ORDER DIAGNOSTIC AvailableBalance Balance={Balance:F2} PaperTrading={PaperTrading}", remainingBalance, settings.PaperTrading);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ORDER BLOCKED Gate=AvailableBalance Reason=Balance retrieval failed.");
                return;
            }

            if (remainingBalance <= 0)
            {
                _logger.LogWarning("ORDER BLOCKED Gate=AvailableBalance Reason=Balance is zero or negative. Balance={Balance:F2}", remainingBalance);
                return;
            }

            await ProcessBuyOrders(gainers, remainingBalance, jwtToken);
        }

        private async Task<decimal> ProcessStock(Gainer stock, decimal remainingBalance, string jwtToken)
        {
            _logger.LogDebug("ORDER DIAGNOSTIC Candidate Symbol={Symbol} Token={Token} Price={Price:F4} ChangePercent={ChangePercent:F3} Volume={Volume} IsOwned={IsOwned} InstrumentType={InstrumentType} EMA9={EMA9:F6} EMA21={EMA21:F6} CrossoverPending={CrossoverPending} Signal={Signal}",
                stock.Symbol, stock.SymbolToken, stock.CurrentPrice, stock.ChangePercent,
                Math.Max(stock.TickDayVolume, stock.Quote?.DayVolume ?? 0L), stock.IsOwned,
                stock.InstrumentType, stock.Ema9, stock.Ema21, stock.EmaCrossoverPending, stock.Signal);
            if (stock.IsOwned) { _logger.LogInformation("ORDER BLOCKED Symbol={Symbol} Gate=AlreadyOwned", stock.Symbol); _tradeManager.ReleaseSymbol(stock.Symbol); stock.Suggestion = "Already owned."; return remainingBalance; }
            if (!_tradingSettingsProvider.Current.IgnoreMarketHours && !_marketClock.IsNewIntradayEntryWindowOpen) { _logger.LogInformation("ORDER BLOCKED Symbol={Symbol} Gate=MarketHours", stock.Symbol); stock.Suggestion = "New entry window is closed."; _tradeManager.ReleaseSymbol(stock.Symbol); return remainingBalance; }
            if (stock.InstrumentType != InstrumentType.Equity) { _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=InstrumentType InstrumentType={InstrumentType}", stock.Symbol, stock.InstrumentType); stock.Suggestion = "Only equity BUY candidates are supported."; _tradeManager.ReleaseSymbol(stock.Symbol); return remainingBalance; }
            try
            {
                if (_tradeManager.IsOpen(stock.Symbol)) { _logger.LogInformation("ORDER BLOCKED Symbol={Symbol} Gate=ExistingOpenTrade", stock.Symbol); stock.Suggestion = "Close the existing position before buying again."; _tradeManager.ReleaseSymbol(stock.Symbol); return remainingBalance; }
                if (stock.CurrentPrice <= 0m)
                {
                    _logger.LogInformation("ORDER BLOCKED Symbol={Symbol} Gate=CurrentPrice Price={Price:F4}", stock.Symbol, stock.CurrentPrice);
                    stock.Suggestion = "Waiting for a valid market price.";
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }

                var activityVolume = Math.Max(
                    stock.TickDayVolume,
                    Math.Max(stock.Quote?.TradeVolume ?? 0L,
                        Math.Max(stock.Quote?.DayVolume ?? 0L, Math.Max(stock.Quote?.Volume ?? 0L, stock.Quote?.LastTradedQuantity ?? 0L))));
                if (activityVolume <= 0)
                {
                    _logger.LogInformation("ORDER BLOCKED Symbol={Symbol} Gate=ActivityVolume Volume={Volume}", stock.Symbol, activityVolume);
                    stock.Suggestion = "Waiting for active trading volume.";
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }

                var strategySnapshot = _emaStrategy.GetSnapshot(stock.SymbolToken);
                _logger.LogInformation(
                    "EMA BUY CONSUMER STATE Symbol={Symbol} Token={Token} SnapshotFound={SnapshotFound} SnapshotEMA9={SnapshotEMA9:F8} SnapshotEMA21={SnapshotEMA21:F8} SnapshotGapPercent={SnapshotGapPercent:F8} SnapshotEntrySignalPending={SnapshotEntrySignalPending} SnapshotCandleTimeIST={SnapshotCandleTimeIST} WatchlistEMA9={WatchlistEMA9:F8} WatchlistEMA21={WatchlistEMA21:F8} WatchlistCrossoverPending={WatchlistCrossoverPending} CurrentPrice={CurrentPrice:F6} ChangePercent={ChangePercent:F3} ActivityVolume={ActivityVolume}",
                    stock.Symbol, stock.SymbolToken, strategySnapshot != null,
                    strategySnapshot?.Ema9 ?? 0m, strategySnapshot?.Ema21 ?? 0m,
                    strategySnapshot?.GapPercent ?? 0m,
                    strategySnapshot?.EntrySignalPending ?? false,
                    strategySnapshot?.CandleTimeIst.ToString("yyyy-MM-dd HH:mm:ss") ?? "none",
                    stock.Ema9, stock.Ema21, stock.EmaCrossoverPending,
                    stock.CurrentPrice, stock.ChangePercent, activityVolume);

                if (!_emaStrategy.TryConsumeEntrySignal(stock.SymbolToken, out var emaSignal))
                {
                    stock.Suggestion = $"Waiting for a completed candle to close with EMA9 crossing above EMA21. EMA9={stock.Ema9:F6}, EMA21={stock.Ema21:F6}";
                    _logger.LogInformation("ORDER BLOCKED Symbol={Symbol} Gate=EMAEntrySignal CrossoverPending={CrossoverPending} EMA9={EMA9:F6} EMA21={EMA21:F6} GapPercent={GapPercent:F6} Suggestion={Suggestion}", stock.Symbol, stock.EmaCrossoverPending, stock.Ema9, stock.Ema21, stock.EmaGapPercent, stock.Suggestion);
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }

                stock.Suggestion = $"Completed-candle EMA9/EMA21 bullish crossover. EMA9={emaSignal.Ema9:F6}, EMA21={emaSignal.Ema21:F6}";
                stock.Reason = "EMA9 crossed above EMA21 at completed one-minute candle close.";
                _logger.LogInformation(
                    "EMA CANDLE-CLOSE CROSSOVER BUY SIGNAL Symbol={Symbol} ChangePercent={ChangePercent:F3} Volume={Volume} EMA9={EMA9:F6} EMA21={EMA21:F6} GapPercent={GapPercent:F6} CHOP14={CHOP14:F2} CHOPThreshold={CHOPThreshold:F2} StochRSI_K={StochRSIK:F2} StochRSI_D={StochRSID:F2} PullbackLow={PullbackLow:F6} RecoverySwingHigh={RecoverySwingHigh:F6} RecoveryTimeIST={CandleTime}",
                    stock.Symbol, stock.ChangePercent, activityVolume, emaSignal.Ema9, emaSignal.Ema21,
                    emaSignal.GapPercent, emaSignal.Choppiness, 61.8m, emaSignal.StochRsiK,
                    emaSignal.StochRsiD, emaSignal.PullbackLow, emaSignal.RecoverySwingHigh, emaSignal.CandleTimeIst);

                var candidate = new TradeCandidate
                {
                    Stock = stock,
                    EntryDecision = "EMA_CROSSOVER_CANDLE_CLOSE",
                    SignalPrice = emaSignal.CandleClosePrice > 0m ? emaSignal.CandleClosePrice : stock.CurrentPrice,
                    SignalAtr = stock.ATR,
                    SignalTimeUtc = DateTime.UtcNow,
                    BuyEligibilityReason = "EMA_CROSSOVER_CANDLE_CLOSE"
                };
                return await ExecuteTradeCandidate(candidate, remainingBalance, jwtToken);
            }
            catch (Exception ex) { _logger.LogError(ex, "ORDER FAILED Gate=ProcessStock Symbol={Symbol}; exception prevented order processing.", stock.Symbol); _tradeManager.ReleaseSymbol(stock.Symbol); return remainingBalance; }
        }

        private async Task ProcessBuyOrders(List<Gainer> gainers, decimal remainingBalance, string jwtToken)
        {
            foreach (var stock in gainers)
            {
                remainingBalance = await ProcessStock(stock, remainingBalance, jwtToken);

                if (remainingBalance <= 0)
                    break;
            }
        }

        private async Task<(int Quantity, decimal Margin)> GetAffordableQuantity(
            Gainer stock,
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
                    stock.SymbolToken, quantity, stock.CurrentPrice,
                    stock.Exchange, "INTRADAY", "BUY");
                _logger.LogInformation(
                    "INTRADAY MARGIN CHECK Symbol={Symbol} Token={Token} Exchange={Exchange} ProductType=INTRADAY OrderType=MARKET Quantity={Quantity} ReferencePrice={Price:F4} BrokerRequiredMargin={Margin:F2} AvailableFunds={Funds:F2}",
                    stock.Symbol, stock.SymbolToken, stock.Exchange, quantity, stock.CurrentPrice, margin, availableBalance);

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

        private async Task<decimal> GetLowestPriceChangeAsync(Gainer stock, string jwtToken)
        {
            var candleCount = Math.Clamp(_tradingSettingsProvider.Current.ChargeCoverAveragePriceChangeCandles, 2, 100);
            var candles = await _historicalDataService.GetAsync(stock.SymbolToken, jwtToken, "ONE_MINUTE", stock.Exchange);
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
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=LowestPriceChange Reason=InsufficientCompletedOneMinuteCandles Required={Required} Available={Available}", stock.Symbol, candleCount, completed.Count);
                return 0m;
            }

            var recent = completed.TakeLast(candleCount).ToList();
            var changes = recent.Select(c => Math.Abs(c.Close - c.Open)).ToList();
            var lowestChange = changes.Min();
            _logger.LogInformation("CHARGE COVER LOWEST PRICE CHANGE Symbol={Symbol} CandleCount={CandleCount} LowestAbsoluteCandleBodyChange={LowestChange:F6} LastClose={LastClose:F4} FirstOpen={FirstOpen:F4}", stock.Symbol, candleCount, lowestChange, recent[^1].Close, recent[0].Open);
            return lowestChange;
        }

        private async Task<int> FindMinimumChargeCoverQuantityAsync(Gainer stock, decimal price, decimal increment)
        {
            if (increment <= 0m)
                throw new InvalidOperationException("Lowest absolute candle-body price change over configured one-minute candles must be greater than zero.");

            // The broker charge-estimate endpoint is rate-limited. Estimate the
            // fixed and variable components from only two quantities per side,
            // then solve locally instead of making an API request on every search
            // iteration. Add a 15% safety buffer to avoid underestimating charges.
            const int sampleQuantity = 1000;
            var buyOne = await _brokerage.CalculateAsync(stock.Symbol, stock.SymbolToken, price, 1,
                BrokerageTransactionType.Buy, stock.Exchange, "INTRADAY");
            var sellOne = await _brokerage.CalculateAsync(stock.Symbol, stock.SymbolToken, price + increment, 1,
                BrokerageTransactionType.Sell, stock.Exchange, "INTRADAY");
            var buySample = await _brokerage.CalculateAsync(stock.Symbol, stock.SymbolToken, price, sampleQuantity,
                BrokerageTransactionType.Buy, stock.Exchange, "INTRADAY");
            var sellSample = await _brokerage.CalculateAsync(stock.Symbol, stock.SymbolToken, price + increment, sampleQuantity,
                BrokerageTransactionType.Sell, stock.Exchange, "INTRADAY");

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
                    stock.Symbol, increment, high, EstimatedRoundTripCharges(high));
                return 0;
            }

            while (low < high)
            {
                var middle = low + (high - low) / 2;
                if (increment * middle > EstimatedRoundTripCharges(middle))
                    high = middle;
                else
                    low = middle + 1;
            }

            _logger.LogInformation(
                "CHARGE COVER MODEL Symbol={Symbol} Quantity={Quantity} Increment={Increment:F6} EstimatedRoundTripCharges={Charges:F2} SafetyBufferPercent=15 BuyFixed={BuyFixed:F4} BuyPerUnit={BuyPerUnit:F6} SellFixed={SellFixed:F4} SellPerUnit={SellPerUnit:F6} BrokerEstimateCalls=4",
                stock.Symbol, low, increment, EstimatedRoundTripCharges(low),
                buyModel.Fixed, buyModel.PerUnit, sellModel.Fixed, sellModel.PerUnit);
            return low;
        }

        private int GetAvailableAtCurrentPrice(Gainer stock)
        {
            // For a market BUY, count valid ask-side depth; asks are often above LTP.
            var depth = stock.Quote?.Depth?.Sell;
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

        protected override async Task SyncTradeOrder(OrderStatusMessage order)
        {
            try
            {
                if (order == null ||
                    (!string.Equals(order.TransactionType, "BUY", StringComparison.OrdinalIgnoreCase) &&
                     !string.Equals(order.TransactionType, "SELL", StringComparison.OrdinalIgnoreCase)))
                    return;

                var pending = _tradeManager.GetByIdentity(order.Exchange, order.SymbolToken, order.Symbol);
                if (pending == null ||
                    (pending.Status != TradeStatus.PendingBrokerConfirmation &&
                     pending.Status != TradeStatus.PartiallyFilled))
                    return;

                var expectedEntry = pending.Direction == TradeDirection.Short ? "SELL" : "BUY";
                if (!string.Equals(order.TransactionType, expectedEntry, StringComparison.OrdinalIgnoreCase))
                    return;

                _logger.LogInformation(
                    "ENTRY ORDER UPDATE Symbol={Symbol}, Token={Token}, OrderId={OrderId}, Transaction={Transaction}, Status={Status}, ExecutionState={ExecutionState}, Filled={Filled}/{Quantity}, AveragePrice={AveragePrice}",
                    order.Symbol, order.SymbolToken, order.OrderId, order.TransactionType, order.Status,
                    order.ExecutionState, order.FilledQuantity, order.Quantity, order.AveragePrice);

                if (order.ExecutionState == OrderExecutionState.PartiallyFilled)
                {
                    await _tradeManager.UpdatePartialEntryAsync(
                        pending, order.OrderId, order.Quantity, order.FilledQuantity, order.AveragePrice,
                        order.ExecutionState, order.UpdatedTime);
                    return;
                }

                if (order.ExecutionState == OrderExecutionState.Cancelled ||
                    order.ExecutionState == OrderExecutionState.Rejected ||
                    string.Equals(order.Status, "REJECTED", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(order.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(order.Status, "CANCELED", StringComparison.OrdinalIgnoreCase))
                {
                    if (order.FilledQuantity > 0 && order.AveragePrice > 0)
                        await ConfirmEntryFromBrokerUpdateAsync(pending, order);
                    else
                        await _tradeManager.MarkRejectedAsync(pending, order.Status);
                    return;
                }

                if (!string.Equals(order.Status, "COMPLETE", StringComparison.OrdinalIgnoreCase) &&
                    order.ExecutionState != OrderExecutionState.Complete)
                    return;

                await ConfirmEntryFromBrokerUpdateAsync(pending, order);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ENTRY websocket processing failed for {Symbol}.", order?.Symbol);
                if (!string.IsNullOrWhiteSpace(order?.Symbol))
                    _tradeManager.ReleaseSymbol(order.Symbol);
            }
        }

        private async Task ConfirmEntryFromBrokerUpdateAsync(TradeOrder trade, OrderStatusMessage order)
        {
            var entryPrice = order.AveragePrice > 0 ? order.AveragePrice : trade.EntryPrice;
            var quantity = order.FilledQuantity > 0 ? order.FilledQuantity : trade.EntryRequestedQuantity > 0 ? trade.EntryRequestedQuantity : trade.Quantity;
            var watch = _watchList.Get(trade.SymbolToken);
            var atr = watch?.ATR > 0 ? watch.ATR : entryPrice * 0.005m;
            var instrumentSettings = _tradingSettingsProvider.Current.Equity;
            var calculatedStop = TradeLevelCalculator.CalculateProtectiveStop(
                entryPrice,
                atr,
                instrumentSettings.AtrStopMultiplier,
                instrumentSettings.MinimumStopPercent,
                instrumentSettings.MaximumStopPercent);
            // The broker average fill may differ from the signal LTP. Recompute
            // the protective stop from the actual fill; retaining the pre-order
            // stop can leave risk too wide or too tight after slippage.
            var stopLoss = trade.Direction == TradeDirection.Short
                ? OrderPriceValidator.RoundUp(calculatedStop)
                : OrderPriceValidator.RoundDown(calculatedStop);
            var chargesPerUnit = quantity > 0 ? trade.EstimatedCharges / quantity : 0m;
            var breakEven = trade.Direction == TradeDirection.Short
                ? OrderPriceValidator.RoundDown(entryPrice - chargesPerUnit)
                : OrderPriceValidator.RoundUp(entryPrice + chargesPerUnit);
            trade.InitialStopLoss = stopLoss;
            trade.InitialRiskPerShare = trade.Direction == TradeDirection.Short
                ? Math.Max(0m, stopLoss - entryPrice)
                : Math.Max(0m, entryPrice - stopLoss);

            trade.ATR = atr;
            IndicatorMapper.ApplyTradeIndicators(trade, watch);

            if (!_tradeManager.ConfirmBuy(
                    trade.Symbol, order.OrderId, trade.UniqueOrderId, quantity, entryPrice,
                    stopLoss, breakEven, breakEven, trade.EstimatedCharges,
                    trade.EstimatedGrossProfit, trade.EstimatedNetProfit, trade.ChargesPercent,
                    out var confirmedTrade))
                return;

            // ConfirmBuy may materialize a fresh TradeOrder instance. Re-apply the
            // execution-time indicator snapshot before persisting/emailing so the
            // BUY notification never falls back to the default all-zero snapshot.
            IndicatorMapper.ApplyTradeIndicators(confirmedTrade!, watch, captureEntrySnapshot: false);

            _watchList.MarkOwned(
                confirmedTrade!.SymbolToken, confirmedTrade.Quantity, confirmedTrade.EntryPrice,
                confirmedTrade.StopLoss);
            await _tradeManager.SaveAsync(confirmedTrade);

            try
            {
                var buyEmailBody = EmailHelper.BuildBuyOrderEmail(
                    confirmedTrade,
                    _tradingSettingsProvider.Current.PaperTrading
                        && !_tradingSettingsProvider.Current.PaperTradingAsLive);
                if (_pendingBuyEmailDetails.TryRemove(confirmedTrade.SymbolToken, out var quantityDetails))
                    buyEmailBody += quantityDetails;
                buyEmailBody += BuildEmaEntryEmailDetails(confirmedTrade);
                _logger.LogInformation("ORDER EMAIL MODE Symbol={Symbol} PaperTrading={PaperTrading} PaperTradingAsLive={PaperTradingAsLive} EmailPresentedAsPaper={EmailPresentedAsPaper}",
                    confirmedTrade.Symbol, _tradingSettingsProvider.Current.PaperTrading,
                    _tradingSettingsProvider.Current.PaperTradingAsLive,
                    _tradingSettingsProvider.Current.PaperTrading && !_tradingSettingsProvider.Current.PaperTradingAsLive);
                EmailHelper.SendEmail(
                    buyEmailBody,
                    _tradingSettingsProvider.Current,
                    $"{confirmedTrade.EntryTransactionType} EXECUTED {confirmedTrade.Symbol}",
                    true);
            }
            catch (Exception emailEx)
            {
                _logger.LogWarning(emailEx, "Entry notification failed for {Symbol}; trade remains confirmed.", confirmedTrade.Symbol);
            }
        }

        private string BuildEmaEntryEmailDetails(TradeOrder trade)
        {
            var crossover = _emaStrategy.GetSnapshot(trade.SymbolToken);
            var entryTimeUtc = trade.EntryTime.Kind == DateTimeKind.Utc
                ? trade.EntryTime
                : DateTime.SpecifyKind(trade.EntryTime, DateTimeKind.Utc);
            TimeZoneInfo indiaTimeZone;
            try { indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
            catch (TimeZoneNotFoundException) { indiaTimeZone = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
            var entryTimeIst = TimeZoneInfo.ConvertTimeFromUtc(entryTimeUtc, indiaTimeZone);

            var ema9 = trade.EntryEma9 > 0m ? trade.EntryEma9 : crossover?.Ema9 ?? 0m;
            var ema21 = trade.EntryEma21 > 0m ? trade.EntryEma21 : crossover?.Ema21 ?? 0m;
            var gapPercent = trade.EntryEmaGapPercent != 0m ? trade.EntryEmaGapPercent : crossover?.GapPercent ?? 0m;
            var crossoverTime = trade.EmaCrossoverTime ?? crossover?.CandleTimeIst;
            var chop = trade.EntryChoppinessIndex >= 0m ? trade.EntryChoppinessIndex : crossover?.Choppiness ?? -1m;
            var stochK = trade.EntryStochRsiK >= 0m ? trade.EntryStochRsiK : crossover?.StochRsiK ?? -1m;
            var stochD = trade.EntryStochRsiD >= 0m ? trade.EntryStochRsiD : crossover?.StochRsiD ?? -1m;
            var pullbackLow = trade.EntryPullbackLow > 0m ? trade.EntryPullbackLow : crossover?.PullbackLow ?? 0m;
            var swingHigh = trade.EntryRecoverySwingHigh > 0m ? trade.EntryRecoverySwingHigh : crossover?.RecoverySwingHigh ?? 0m;

            return $"<hr/><h3>EMA9/EMA21 Candle-Close Crossover</h3>" +
                $"<p>Entry time (IST): {entryTimeIst:yyyy-MM-dd HH:mm:ss}</p>" +
                $"<p><b>Entry rule</b><br/>Buy signal generated when a completed one-minute candle closes with EMA9 crossing above EMA21. No pullback or recovery confirmation is required.</p>" +
                $"<p><b>EMA values</b><br/>EMA9: {ema9:F6}<br/>EMA21: {ema21:F6}<br/>EMA gap: {gapPercent:F6}%<br/>Crossover candle time (IST): {(crossoverTime.HasValue ? crossoverTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Unavailable")}</p>" +
                $"<p><b>Stop loss</b><br/>Configured stop loss: 0.1% below entry, rounded down to the nearest ₹0.01 tick.</p>" +
                $"<p><b>Choppiness Index (CHOP 14)</b><br/>Value: {(chop >= 0m ? chop.ToString("F2") : "Unavailable")}<br/>Reference threshold: 61.8 (informational only; not an entry filter).</p>" +
                $"<p><b>Stochastic RSI (informational)</b><br/>%K: {(stochK >= 0m ? stochK.ToString("F2") : "Unavailable")}<br/>%D: {(stochD >= 0m ? stochD.ToString("F2") : "Unavailable")}</p>" +
                $"<p><b>Recent price-action snapshot</b><br/>Pullback low: {(pullbackLow > 0m ? pullbackLow.ToString("F4") : "Unavailable")}<br/>Recovery swing high: {(swingHigh > 0m ? swingHigh.ToString("F4") : "Unavailable")}</p>";
        }

        private async Task<decimal> ExecuteTradeCandidate(
            TradeCandidate candidate,
            decimal remainingBalance,
            string jwtToken)
        {
            var stock = candidate.Stock;
            var config = _tradingSettingsProvider.Current;

            _logger.LogInformation("ORDER DIAGNOSTIC ValidationStarted Symbol={Symbol} Token={Token} Price={Price:F4} ATR={ATR:F4} AvailableBalance={Balance:F2} RiskPercent={RiskPercent} MaxCapitalPerTradePercent={MaxCapitalPercent} PaperTrading={PaperTrading} AutoTrading={AutoTrading}", stock.Symbol, stock.SymbolToken, stock.CurrentPrice, stock.ATR, remainingBalance, config.RiskPercentage, config.MaxCapitalPerTradePercent, config.PaperTrading, config.EnableAutoTrading);
            if (stock.CurrentPrice <= 0m)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=CurrentPrice Price={Price}", stock.Symbol, stock.CurrentPrice);
                stock.Suggestion = "Invalid market price.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            const decimal stopLossPercent = 0.10m;
            stock.StopLoss = Math.Floor(stock.CurrentPrice * (1m - stopLossPercent / 100m) * 100m) / 100m;
            _logger.LogInformation("STOP LOSS CONFIG Symbol={Symbol} EntryPrice={EntryPrice:F4} StopLossPercent={StopLossPercent:F3} StopLoss={StopLoss:F4}", stock.Symbol, stock.CurrentPrice, stopLossPercent, stock.StopLoss);
            if (stock.StopLoss <= 0m || stock.StopLoss >= stock.CurrentPrice)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=ProtectiveStop Price={Price:F4} StopLoss={StopLoss:F4} StopLossPercent={StopLossPercent:F3}", stock.Symbol, stock.CurrentPrice, stock.StopLoss, stopLossPercent);
                stock.Suggestion = "Protective stop could not be calculated.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            decimal lowestPriceChange;
            try
            {
                lowestPriceChange = await GetLowestPriceChangeAsync(stock, jwtToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ORDER BLOCKED Symbol={Symbol} Gate=LowestPriceChange Reason=OneMinuteCandleFetchFailed", stock.Symbol);
                lowestPriceChange = 0m;
            }
            if (lowestPriceChange <= 0m)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=ChargeCoverQuantity Price={Price:F4} LowestPriceChange={LowestPriceChange:F6} Reason=NoUsableCandleChange", stock.Symbol, stock.CurrentPrice, lowestPriceChange);
                stock.Suggestion = "Could not calculate average price change from the configured number of completed one-minute candles.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var minimumQuantity = await FindMinimumChargeCoverQuantityAsync(stock, stock.CurrentPrice, lowestPriceChange);
            if (minimumQuantity <= 0)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=ChargeCoverQuantity Price={Price:F4} LowestPriceChange={LowestPriceChange:F6} Reason=NoQuantityFound", stock.Symbol, stock.CurrentPrice, lowestPriceChange);
                stock.Suggestion = "Could not find a quantity where the average recent one-minute price change covers round-trip charges.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var availableAtPrice = GetAvailableAtCurrentPrice(stock);
            _logger.LogInformation("ORDER DIAGNOSTIC ChargeCoverAndDepth Symbol={Symbol} MinimumChargeCoverQuantity={MinimumQuantity} AvailableAskQuantityAtCurrentPrice={AvailableQuantity} CurrentPrice={Price:F4} LowestPriceChange={LowestPriceChange:F6} PriceChangeLookbackCandles={CandleCount}", stock.Symbol, minimumQuantity, availableAtPrice, stock.CurrentPrice, lowestPriceChange, config.ChargeCoverAveragePriceChangeCandles);
            if (availableAtPrice < minimumQuantity)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=MarketDepth MinimumRequiredQuantity={MinimumQuantity} AvailableAtCurrentPrice={AvailableQuantity} Price={Price:F4}", stock.Symbol, minimumQuantity, availableAtPrice, stock.CurrentPrice);
                stock.Suggestion = $"Need at least {minimumQuantity} shares for charge cover, but only {availableAtPrice} are shown at the current price.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var affordable = await GetAffordableQuantity(stock, minimumQuantity, availableAtPrice, remainingBalance);
            var quantity = affordable.Quantity;
            var requiredMargin = affordable.Margin;
            if (quantity < minimumQuantity)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=AffordableQuantity MinimumRequiredQuantity={MinimumQuantity} AvailableAtPrice={AvailableAtPrice} AvailableBalance={Balance:F2} MarginReturned={Margin:F2}", stock.Symbol, minimumQuantity, availableAtPrice, remainingBalance, requiredMargin);
                stock.Suggestion = $"Available funds cannot cover the minimum charge-cover quantity of {minimumQuantity}; quantity will not be reduced below that minimum.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var depthLevels = stock.Quote?.Depth?.Sell?
                .Where(x => x.Price > 0m && x.Quantity > 0)
                .OrderBy(x => x.Price)
                .ToList() ?? new();
            var executableAskPrice = depthLevels.Count > 0 ? depthLevels.Max(x => (decimal)x.Price) : 0m;
            var lowestChangePercent = stock.CurrentPrice > 0m ? lowestPriceChange / stock.CurrentPrice * 100m : 0m;
            var estimatedBuyCharges = await _brokerage.CalculateAsync(stock.Symbol, stock.SymbolToken, stock.CurrentPrice, quantity,
                BrokerageTransactionType.Buy, stock.Exchange, "INTRADAY");
            var estimatedSellCharges = await _brokerage.CalculateAsync(stock.Symbol, stock.SymbolToken, stock.CurrentPrice + lowestPriceChange, quantity,
                BrokerageTransactionType.Sell, stock.Exchange, "INTRADAY");
            var estimatedBuyTotal = stock.CurrentPrice * quantity + estimatedBuyCharges.TotalCharges;
            var estimatedSellTotal = (stock.CurrentPrice + lowestPriceChange) * quantity + estimatedSellCharges.TotalCharges;
            var estimatedRoundTripCharges = estimatedBuyCharges.TotalCharges + estimatedSellCharges.TotalCharges;
            var estimatedDifference = estimatedSellTotal - estimatedBuyTotal;
            var quantityEmailDetails =
                $"<hr/><h3>Charge-Cover Quantity & Market Availability</h3>" +
                $"<p><b>Lowest price-change method</b><br/>Lookback: {config.ChargeCoverAveragePriceChangeCandles} completed 1-minute candles<br/>Lowest absolute candle-body change (|Close − Open|): ₹{lowestPriceChange:F6}<br/>Lowest change as percentage of entry reference price: {lowestChangePercent:F4}%<br/>Price-change increment used in charge-cover calculation: ₹{lowestPriceChange:F6}</p>" +
                $"<p><b>Charge-cover calculation</b><br/>Buy total = (entry price × quantity) + buy charges<br/>Sell total = ((entry price + ₹{lowestPriceChange:F6}) × quantity) + sell charges<br/>Minimum quantity required for estimated price difference to exceed total buy + sell charges: {minimumQuantity:N0} shares<br/>Condition: Sell total − Buy total &gt; buy charges + sell charges.<br/>For final requested quantity ({quantity:N0}): estimated buy charges ₹{estimatedBuyCharges.TotalCharges:F2}; estimated sell charges ₹{estimatedSellCharges.TotalCharges:F2}; estimated round-trip charges ₹{estimatedRoundTripCharges:F2}; estimated buy total ₹{estimatedBuyTotal:F2}; estimated sell total ₹{estimatedSellTotal:F2}; estimated difference ₹{estimatedDifference:F2}; charge-cover condition {(lowestPriceChange * quantity > estimatedRoundTripCharges ? "PASSED" : "NOT PASSED")}.</p>" +
                $"<p><b>Market depth at entry reference price</b><br/>Side checked: ASK / sell-side depth for a BUY<br/>Entry reference price (LTP): ₹{stock.CurrentPrice:F4}<br/>Highest ask price included in available depth: {(executableAskPrice > 0m ? $"₹{executableAskPrice:F4}" : "Unavailable")}<br/>Available quantity at eligible ask levels: {availableAtPrice:N0} shares</p>" +
                $"<p><b>Final quantity and intraday margin</b><br/>Final requested quantity: {quantity:N0} shares (must not be below minimum charge-cover quantity)<br/>Quantity is the maximum affordable quantity within eligible market depth and available funds.<br/>Estimated broker INTRADAY margin: ₹{requiredMargin:F2}<br/>Available funds before order: ₹{remainingBalance:F2}<br/>Quantity selection: max affordable quantity, bounded by available eligible depth; minimum required quantity is never silently reduced.</p>";

            var validation = await _tradeValidation.ValidateAsync(stock, quantity);
            if (!validation.CanTrade)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=TradeValidation Reason={Reason} Quantity={Quantity} Price={Price:F4}", stock.Symbol, validation.Suggestion, quantity, stock.CurrentPrice);
                stock.Suggestion = validation.Suggestion;
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            // The only entry validation remaining is charge-inclusive monetary risk.
            var estimatedRisk = Math.Max(0m, validation.NetRisk);
            var maximumTotalOpenRisk = config.MaximumTotalOpenRisk;
            if (maximumTotalOpenRisk > 0m && estimatedRisk > maximumTotalOpenRisk)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=MaximumTotalOpenRisk EstimatedNetRisk={EstimatedRisk:F2} Cap={RiskCap:F2} GrossProfit={GrossProfit:F2} NetProfit={NetProfit:F2} Charges={Charges:F2}", stock.Symbol, estimatedRisk, maximumTotalOpenRisk, validation.GrossProfit, validation.NetProfit, validation.Charges.TotalCharges);
                stock.Suggestion = $"Hard monetary risk cap exceeded. Net risk ₹{estimatedRisk:F2} > cap ₹{maximumTotalOpenRisk:F2}.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            if (!await _intradayRiskGuard.TryReserveAsync(stock, estimatedRisk))
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=RiskReservation EstimatedRisk={EstimatedRisk:F2}", stock.Symbol, estimatedRisk);
                stock.Suggestion = $"Hard monetary risk cap reached. Charge-inclusive risk ₹{estimatedRisk:F2}.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            candidate.Stock = stock;
            candidate.EntryIndicators = IndicatorMapper.ToTradeIndicatorSnapshot(stock);
            if (string.IsNullOrWhiteSpace(candidate.BuyEligibilityReason))
                candidate.BuyEligibilityReason = "EMA_CROSSOVER_CANDLE_CLOSE";
            candidate.EligibilityReason = candidate.BuyEligibilityReason;

            _logger.LogInformation("ORDER DIAGNOSTIC ValidationPassed Symbol={Symbol} Quantity={Quantity} RequiredMargin={RequiredMargin:F2} Price={Price:F4} StopLoss={StopLoss:F4} EstimatedCharges={Charges:F2} EstimatedNetProfit={NetProfit:F2} EstimatedNetRisk={NetRisk:F2} PaperTrading={PaperTrading} AutoTrading={AutoTrading}", stock.Symbol, quantity, requiredMargin, stock.CurrentPrice, stock.StopLoss, validation.Charges.TotalCharges, validation.NetProfit, estimatedRisk, config.PaperTrading, config.EnableAutoTrading);

            if (config.PaperTrading)
            {
                _logger.LogInformation("ORDER ROUTE Symbol={Symbol} Route=PaperTrading", stock.Symbol);
                return await HandlePaperTradeAsync(stock, candidate, quantity, requiredMargin, validation, stock, remainingBalance, quantityEmailDetails);
            }

            if (!config.EnableAutoTrading)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=AutoTradingDisabled PaperTrading={PaperTrading} AutoTrading={AutoTrading}", stock.Symbol, config.PaperTrading, config.EnableAutoTrading);
                stock.Suggestion = "Auto trading is disabled.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            _pendingBuyEmailDetails[stock.SymbolToken] = quantityEmailDetails;
            return await PlaceBuyOrderAsync(stock, candidate, quantity, requiredMargin, validation, stock, remainingBalance);
        }

        private async Task<decimal> HandlePaperTradeAsync(
            Gainer stock,
            TradeCandidate candidate,
            int quantity,
            decimal requiredMargin,
            TradeValidationResult validation,
            Gainer latest,
            decimal remainingBalance,
            string quantityEmailDetails)
        {
            // Simulate market execution on the executable side of the quote:
            // long entries buy at Ask, short entries sell at Bid. Using LTP can
            // materially misstate paper fills when the last trade is stale or
            // sits outside the current bid/ask spread.
            var paperEntryPrice = stock.Direction == TradeDirection.Short
                ? (stock.TickBid > 0m ? stock.TickBid : stock.Bid > 0m ? stock.Bid : stock.CurrentPrice)
                : (stock.TickAsk > 0m ? stock.TickAsk : stock.Ask > 0m ? stock.Ask : stock.CurrentPrice);
            var entryPriceDelta = paperEntryPrice - stock.CurrentPrice;
            var paperStopLoss = stock.StopLoss > 0m ? stock.StopLoss + entryPriceDelta : 0m;
            var paperBreakEven = stock.Direction == TradeDirection.Short
                ? OrderPriceValidator.RoundDown(paperEntryPrice - (validation.Charges.TotalCharges / quantity))
                : OrderPriceValidator.RoundUp(paperEntryPrice + (validation.Charges.TotalCharges / quantity));

            _logger.LogInformation(
                "PAPER ENTRY EXECUTION REFERENCE Symbol={Symbol} LTP={LTP:F6} Bid={Bid:F6} Ask={Ask:F6} EntryReference={EntryReference:F6} EntrySide={EntrySide} PriceDelta={PriceDelta:F6}",
                stock.Symbol, stock.CurrentPrice, stock.TickBid > 0m ? stock.TickBid : stock.Bid,
                stock.TickAsk > 0m ? stock.TickAsk : stock.Ask, paperEntryPrice,
                stock.Direction == TradeDirection.Short ? "BID" : "ASK", entryPriceDelta);

            var paperTrade = new TradeOrder
            {
                Symbol = stock.Symbol.Trim().ToUpperInvariant(),
                SymbolToken = stock.SymbolToken,
                Quantity = quantity,
                EntryRequestedQuantity = quantity,
                Direction = stock.Direction,
                EntryTransactionType = stock.EntryTransactionType,
                ExitTransactionType = stock.Direction == TradeDirection.Short ? "BUY" : "SELL",
                InstrumentType = stock.InstrumentType,
                Exchange = stock.Exchange,
                ProductType = stock.ProductType,
                OrderType = stock.OrderType,
                Duration = stock.Duration,
                EntryPrice = paperEntryPrice,
                EntryTime = DateTime.UtcNow,
                EntryEma9 = _emaStrategy.GetSnapshot(stock.SymbolToken)?.Ema9 ?? stock.Ema9,
                EntryEma21 = _emaStrategy.GetSnapshot(stock.SymbolToken)?.Ema21 ?? stock.Ema21,
                EntryEmaGapPercent = _emaStrategy.GetSnapshot(stock.SymbolToken)?.GapPercent ?? stock.EmaGapPercent,
                EmaCrossoverTime = _emaStrategy.GetSnapshot(stock.SymbolToken)?.CandleTimeIst,
                EntryChoppinessIndex = _emaStrategy.GetSnapshot(stock.SymbolToken)?.Choppiness ?? -1m,
                EntryStochRsiK = _emaStrategy.GetSnapshot(stock.SymbolToken)?.StochRsiK ?? -1m,
                EntryStochRsiD = _emaStrategy.GetSnapshot(stock.SymbolToken)?.StochRsiD ?? -1m,
                EntryPullbackLow = _emaStrategy.GetSnapshot(stock.SymbolToken)?.PullbackLow ?? 0m,
                EntryRecoverySwingHigh = _emaStrategy.GetSnapshot(stock.SymbolToken)?.RecoverySwingHigh ?? 0m,
                EntryIndicators = IndicatorMapper.ToTradeIndicatorSnapshot(latest),
                HighestPrice = paperEntryPrice,
                LowestPrice = paperEntryPrice,
                StopLoss = paperStopLoss,
                InitialStopLoss = paperStopLoss,
                InitialRiskPerShare = stock.Direction == TradeDirection.Short
                    ? Math.Max(0m, paperStopLoss - paperEntryPrice)
                    : Math.Max(0m, paperEntryPrice - paperStopLoss),
                EstimatedCharges = validation.Charges.TotalCharges,
                EstimatedEntryCharges = validation.EntryCharges.TotalCharges,
                EstimatedExitCharges = validation.ExitCharges.TotalCharges,
                BreakEvenPrice = paperBreakEven,
                MinimumExitPrice = paperBreakEven,
                EstimatedGrossProfit = validation.GrossProfit,
                EstimatedNetProfit = validation.NetProfit,
                ChargesPercent = validation.ChargePercent,
                EligibilityReason = candidate.BuyEligibilityReason,
                Status = TradeStatus.Open,
                Closed = false,
                ATR = stock.ATR,
                TrailingActivated = false,
                EntryDecision = candidate.EntryDecision,
                EntryReason = candidate.BuyEligibilityReason,
            };

            IndicatorMapper.ApplyTradeIndicators(paperTrade, latest, captureEntrySnapshot: true);

            if (_tradeManager.Contains(paperTrade.Symbol))
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=PaperTradeAlreadyExists", stock.Symbol);
                stock.Suggestion = "Trade already pending.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            _logger.LogInformation("PAPER ORDER CREATE Symbol={Symbol} Quantity={Quantity} Price={Price:F4} StopLoss={StopLoss:F4} RequiredMargin={Margin:F2}", paperTrade.Symbol, quantity, paperTrade.EntryPrice, paperTrade.StopLoss, requiredMargin);
            _tradeManager.GetOrAdd(paperTrade);
            await _tradeManager.SaveAsync(paperTrade);
            _emaStrategy.AcknowledgeEntrySignal(stock.SymbolToken);
            _logger.LogInformation("PAPER ORDER CREATED Symbol={Symbol} TradeStatus={Status}", paperTrade.Symbol, paperTrade.Status);

            _watchList.MarkOwned(
                stock.SymbolToken,
                quantity,
                paperEntryPrice,
                paperStopLoss);

            try
            {
                var buyEmailBody = EmailHelper.BuildBuyOrderEmail(paperTrade, true)
                    + quantityEmailDetails
                    + BuildEmaEntryEmailDetails(paperTrade);
                EmailHelper.SendEmail(
                    buyEmailBody,
                    _tradingSettingsProvider.Current,
                    $"PAPER {paperTrade.EntryTransactionType} EXECUTED {stock.Symbol}",
                    true);
            }
            catch (Exception emailEx)
            {
                _logger.LogWarning(
                    emailEx,
                    "Paper trade notification failed for {Symbol}; trade remains persisted.",
                    stock.Symbol);
            }

            _cachedBalance = Math.Max(0, _cachedBalance - requiredMargin);
            return remainingBalance - requiredMargin;
        }

        private async Task<decimal> PlaceBuyOrderAsync(
            Gainer stock,
            TradeCandidate candidate,
            int quantity,
            decimal requiredMargin,
            TradeValidationResult validation,
            Gainer latest,
            decimal remainingBalance)
        {
            var config = _tradingSettingsProvider.Current;
            var tradeOrder = new TradeOrder
            {
                Symbol = stock.Symbol.Trim().ToUpperInvariant(),
                SymbolToken = stock.SymbolToken,
                Quantity = quantity,
                EntryRequestedQuantity = quantity,
                Direction = stock.Direction,
                EntryTransactionType = stock.EntryTransactionType,
                ExitTransactionType = stock.Direction == TradeDirection.Short ? "BUY" : "SELL",
                InstrumentType = stock.InstrumentType,
                Exchange = stock.Exchange,
                ProductType = stock.ProductType,
                OrderType = stock.OrderType,
                Duration = stock.Duration,
                EntryPrice = stock.CurrentPrice,
                EntryTime = DateTime.UtcNow,
                EntryEma9 = _emaStrategy.GetSnapshot(stock.SymbolToken)?.Ema9 ?? stock.Ema9,
                EntryEma21 = _emaStrategy.GetSnapshot(stock.SymbolToken)?.Ema21 ?? stock.Ema21,
                EntryEmaGapPercent = _emaStrategy.GetSnapshot(stock.SymbolToken)?.GapPercent ?? stock.EmaGapPercent,
                EmaCrossoverTime = _emaStrategy.GetSnapshot(stock.SymbolToken)?.CandleTimeIst,
                EntryChoppinessIndex = _emaStrategy.GetSnapshot(stock.SymbolToken)?.Choppiness ?? -1m,
                EntryStochRsiK = _emaStrategy.GetSnapshot(stock.SymbolToken)?.StochRsiK ?? -1m,
                EntryStochRsiD = _emaStrategy.GetSnapshot(stock.SymbolToken)?.StochRsiD ?? -1m,
                EntryPullbackLow = _emaStrategy.GetSnapshot(stock.SymbolToken)?.PullbackLow ?? 0m,
                EntryRecoverySwingHigh = _emaStrategy.GetSnapshot(stock.SymbolToken)?.RecoverySwingHigh ?? 0m,
                EntryIndicators = IndicatorMapper.ToTradeIndicatorSnapshot(latest),
                HighestPrice = stock.CurrentPrice,
                LowestPrice = stock.CurrentPrice,
                StopLoss = stock.StopLoss,
                InitialStopLoss = stock.StopLoss,
                InitialRiskPerShare = stock.Direction == TradeDirection.Short
                    ? Math.Max(0m, stock.StopLoss - stock.CurrentPrice)
                    : Math.Max(0m, stock.CurrentPrice - stock.StopLoss),
                EstimatedCharges = validation.Charges.TotalCharges,
                EstimatedEntryCharges = validation.EntryCharges.TotalCharges,
                EstimatedExitCharges = validation.ExitCharges.TotalCharges,
                EstimatedGrossProfit = validation.GrossProfit,
                EstimatedNetProfit = validation.NetProfit,
                ChargesPercent = validation.ChargePercent,
                Status = TradeStatus.PendingBrokerConfirmation,
                Closed = false,
                ATR = stock.ATR,
                TrailingActivated = false,
                EntryDecision = candidate.EntryDecision,
                EntryReason = candidate.BuyEligibilityReason,
                EligibilityReason = candidate.BuyEligibilityReason
            };

            IndicatorMapper.ApplyTradeIndicators(tradeOrder, latest, captureEntrySnapshot: true);

            if (_tradeManager.Contains(tradeOrder.Symbol))
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=TradeAlreadyExists", stock.Symbol);
                stock.Suggestion = "Trade already pending.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            if (!_tradeManager.TryCreatePendingTrade(tradeOrder, out _))
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=PendingTradeReservation TradeManager refused pending trade.", stock.Symbol);
                stock.Suggestion = "Trade already active.";
                return remainingBalance;
            }

            _logger.LogInformation("BROKER ORDER SUBMIT Symbol={Symbol} Token={Token} Side={Side} Quantity={Quantity} OrderType={OrderType} ProductType={ProductType} Exchange={Exchange} Duration={Duration} Price={Price:F4} PaperTrading={PaperTrading} AutoTrading={AutoTrading}", stock.Symbol, stock.SymbolToken, stock.Direction == TradeDirection.Short ? "SELL" : "BUY", quantity, stock.OrderType, stock.ProductType, stock.Exchange, stock.Duration, stock.CurrentPrice, config.PaperTrading, config.EnableAutoTrading);
            PlaceOrderResponse buy;
            try
            {
                buy = await _angel.PlaceOrderAsync(
                new PlaceOrderRequest
                {
                    TradingSymbol = stock.Symbol,
                    SymbolToken = stock.SymbolToken,
                    Exchange = stock.Exchange,
                    TransactionType = stock.Direction == TradeDirection.Short ? "SELL" : "BUY",
                    Quantity = quantity,
                    OrderType = stock.OrderType,
                    ProductType = stock.ProductType,
                    Duration = stock.Duration,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BROKER ORDER EXCEPTION Symbol={Symbol} Token={Token} Quantity={Quantity}; broker call threw before a response was received.", stock.Symbol, stock.SymbolToken, quantity);
                await _tradeManager.MarkRejectedAsync(tradeOrder, ex.Message);
                stock.Suggestion = $"Broker order exception: {ex.Message}";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            _logger.LogInformation("BROKER ORDER RESPONSE Symbol={Symbol} Token={Token} Status={Status} Message={Message} OrderId={OrderId} UniqueOrderId={UniqueOrderId}", stock.Symbol, stock.SymbolToken, buy.Status, buy.Message, buy.Data?.OrderId, buy.Data?.UniqueOrderId);
            if (!buy.Status)
            {
                _logger.LogError("BROKER ORDER REJECTED Symbol={Symbol} Token={Token} Reason={Reason}", stock.Symbol, stock.SymbolToken, buy.Message);
                await _tradeManager.MarkRejectedAsync(tradeOrder, buy.Message);
                stock.Suggestion = buy.Message;
                return remainingBalance;
            }

            if (buy.Data == null || string.IsNullOrWhiteSpace(buy.Data.OrderId))
            {
                _logger.LogError(
                    "Broker accepted entry for {Symbol} but returned no order id. Keeping pending trade for reconciliation.",
                    tradeOrder.Symbol);
                _emaStrategy.AcknowledgeEntrySignal(stock.SymbolToken);
                stock.Suggestion = "Order accepted; awaiting broker reconciliation.";
                return remainingBalance - requiredMargin;
            }

            _tradeManager.RefreshTrade(
                tradeOrder.Symbol,
                t =>
                {
                    t.BuyOrderId = buy.Data.OrderId;
                    t.UniqueOrderId = buy.Data.UniqueOrderId;
                });

            _emaStrategy.AcknowledgeEntrySignal(stock.SymbolToken);
            stock.Suggestion = $"{tradeOrder.EntryTransactionType} order submitted.";
            _logger.LogInformation("BROKER ORDER SUBMITTED Symbol={Symbol} OrderId={OrderId} UniqueOrderId={UniqueOrderId} Quantity={Quantity} Status={Status}", tradeOrder.Symbol, buy.Data.OrderId, buy.Data.UniqueOrderId, quantity, tradeOrder.Status);
            _cachedBalance = Math.Max(0, _cachedBalance - requiredMargin);
            return remainingBalance - requiredMargin;
        }


        private static string BuildExecutionEntryReason(Gainer stock)
        {
            return $"Equity entry accepted by risk/charges validation. ProtectiveStop={stock.StopLoss:F2}";
        }

        private string BuildEligibilityReason(Gainer stock, int quantity)
        {
            return $"Risk/charges only; Quantity={quantity}; ProtectiveStop={stock.StopLoss:F2}.";
        }



        private async Task<decimal> GetAvailableBalanceAsync()
        {
            if (_tradingSettingsProvider.Current.PaperTrading)
            {
                var paperBalance = await _paperPortfolio.GetAvailableBalanceAsync();
                _logger.LogDebug("ORDER DIAGNOSTIC PaperPortfolioBalance Balance={Balance:F2}", paperBalance);
                return paperBalance;
            }

            if (DateTime.UtcNow - _lastBalanceRefresh > TimeSpan.FromSeconds(30))
            {
                _logger.LogDebug("ORDER DIAGNOSTIC RefreshingBrokerBalance");
                _cachedBalance = await _angel.GetAvailableBalanceAsync();
                _lastBalanceRefresh = DateTime.UtcNow;
                _logger.LogInformation("ORDER DIAGNOSTIC BrokerBalance Balance={Balance:F2}", _cachedBalance);
            }

            return _cachedBalance;
        }

        private async Task RefreshSubscribedStocksAsync(string jwtToken)
        {
            foreach (var stock in _watchList.Current)
            {
                if (!stock.IsSubscribed || stock.IsOwned || stock.Quote == null)
                    continue;

                IndicatorSnapshot? indicator;
                try
                {
                    indicator = await _indicatorService.GetAsync(stock.Quote, jwtToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "ORDER DIAGNOSTIC IndicatorRefreshException Symbol={Symbol} Token={Token}", stock.Symbol, stock.SymbolToken);
                    continue;
                }
                if (indicator == null)
                {
                    _logger.LogDebug("ORDER DIAGNOSTIC IndicatorRefreshEmpty Symbol={Symbol} Token={Token}", stock.Symbol, stock.SymbolToken);
                    continue;
                }

                // Refresh indicator/candle data only. No strategy evaluator,
                // legacy score/confidence/indicator gate or stock-
                // Indicator refresh is observational only; it does not gate entry.
                IndicatorMapper.ApplyIndicators(stock, indicator);
                stock.HistoricalLoaded = true;
                stock.LastHistoricalUpdate = DateTime.UtcNow;
            }
        }
    }
}
