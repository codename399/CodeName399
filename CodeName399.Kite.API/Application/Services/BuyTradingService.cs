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
        private decimal _cachedBalance;
        private DateTime _lastBalanceRefresh = DateTime.MinValue;

        public BuyTradingService(
            IAngelOneService angel,
            TradingSettingsProvider tradingSettingsProvider,
            TradingStateService state,
            TradeManager tradeManager,
            ITradeOrderRepository tradeHistoryRepository,
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
            if (!settings.IgnoreMarketHours && !_marketClock.IsNewIntradayEntryWindowOpen)
            {
                _logger.LogInformation("ORDER BLOCKED Gate=MarketHours Reason=New-entry window closed. MarketTime={MarketTime} MarketOpen={MarketOpen} MarketClose={MarketClose} EntryCutoff={EntryCutoff}", _marketClock.Now, settings.MarketOpenTime, settings.MarketCloseTime, settings.IntradayEntryCutoffTime);
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

            await ProcessBuyOrders(gainers, remainingBalance);
        }

        private async Task<decimal> ProcessStock(Gainer stock, decimal remainingBalance)
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
                if (stock.CurrentPrice > _tradingSettingsProvider.Current.MaximumStockPrice)
                {
                    _logger.LogInformation("ORDER BLOCKED Symbol={Symbol} Gate=MaximumStockPrice Price={Price:F4} MaximumStockPrice={MaximumStockPrice:F4}", stock.Symbol, stock.CurrentPrice, _tradingSettingsProvider.Current.MaximumStockPrice);
                    stock.Suggestion = $"Stock price exceeds configured maximum ₹{_tradingSettingsProvider.Current.MaximumStockPrice:F2}.";
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }

                if (stock.CurrentPrice <= 0m)
                {
                    _logger.LogInformation("ORDER BLOCKED Symbol={Symbol} Gate=InvalidPrice Price={Price:F4}", stock.Symbol, stock.CurrentPrice);
                    stock.Suggestion = "Waiting for a valid positive price.";
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
                    stock.Suggestion = $"Waiting for a bullish EMA9/EMA21 crossover confirmed at candle close. EMA9={stock.Ema9:F6}, EMA21={stock.Ema21:F6}";
                    _logger.LogInformation("ORDER BLOCKED Symbol={Symbol} Gate=EMAEntrySignal CrossoverPending={CrossoverPending} EMA9={EMA9:F6} EMA21={EMA21:F6} GapPercent={GapPercent:F6} Suggestion={Suggestion}", stock.Symbol, stock.EmaCrossoverPending, stock.Ema9, stock.Ema21, stock.EmaGapPercent, stock.Suggestion);
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }

                stock.Suggestion = $"EMA9/EMA21 bullish crossover confirmed at candle close. EMA9={emaSignal.Ema9:F6}, EMA21={emaSignal.Ema21:F6}";
                stock.Reason = "EMA9 crossed above EMA21 and remained above at the completed candle close.";
                _logger.LogInformation(
                    "EMA CROSSOVER BUY SIGNAL Symbol={Symbol} ChangePercent={ChangePercent:F3} Volume={Volume} EMA9={EMA9:F6} EMA21={EMA21:F6} GapPercent={GapPercent:F6} CandleCloseTimeIST={CandleTime}",
                    stock.Symbol, stock.ChangePercent, activityVolume, emaSignal.Ema9, emaSignal.Ema21,
                    emaSignal.GapPercent, emaSignal.CandleTimeIst);

                var candidate = new TradeCandidate
                {
                    Stock = stock,
                    EntryDecision = "EMA_CROSSOVER_CANDLE_CLOSE",
                    SignalPrice = stock.CurrentPrice,
                    SignalAtr = stock.ATR,
                    SignalTimeUtc = DateTime.UtcNow,
                    BuyEligibilityReason = "EMA_CROSSOVER_CANDLE_CLOSE"
                };
                return await ExecuteTradeCandidate(candidate, remainingBalance);
            }
            catch (Exception ex) { _logger.LogError(ex, "ORDER FAILED Gate=ProcessStock Symbol={Symbol}; exception prevented order processing.", stock.Symbol); _tradeManager.ReleaseSymbol(stock.Symbol); return remainingBalance; }
        }

        private async Task ProcessBuyOrders(List<Gainer> gainers, decimal remainingBalance)
        {
            foreach (var stock in gainers)
            {
                remainingBalance = await ProcessStock(stock, remainingBalance);

                if (remainingBalance <= 0)
                    break;
            }
        }

        private static decimal GetExecutablePrice(Gainer stock) =>
            stock.Direction == TradeDirection.Short
                ? (stock.TickBid > 0m ? stock.TickBid : stock.Bid > 0m ? stock.Bid : stock.CurrentPrice)
                : (stock.TickAsk > 0m ? stock.TickAsk : stock.Ask > 0m ? stock.Ask : stock.CurrentPrice);

        private static long GetAvailableQuantityAtExecutablePrice(
            Gainer stock,
            out decimal depthPrice,
            out string depthSide)
        {
            depthPrice = 0m;
            depthSide = stock.Direction == TradeDirection.Short ? "BID" : "ASK";
            var levels = stock.Direction == TradeDirection.Short
                ? stock.Quote?.Depth?.Buy
                : stock.Quote?.Depth?.Sell;

            if (levels == null || levels.Count == 0)
                return 0;

            var validLevels = levels.Where(x => x.Price > 0m && x.Quantity > 0).ToList();
            if (validLevels.Count == 0)
                return 0;

            // Best executable level: lowest ask for a buy, highest bid for a sell.
            depthPrice = stock.Direction == TradeDirection.Short
                ? validLevels.Max(x => x.Price)
                : validLevels.Min(x => x.Price);

            // Use the market-depth best level itself to cap quantity. Quote/tick
            // ask or bid can update at a different instant and is not used to reject
            // otherwise valid depth at the actual best level.

            // Copy the out parameter to a local variable before using it in a lambda.
            // C# does not allow capturing ref/out/in parameters in lambdas.
            var selectedDepthPrice = depthPrice;
            return validLevels
                .Where(x => x.Price == selectedDepthPrice)
                .Sum(x => x.Quantity);
        }

        private async Task<(int Quantity, decimal Margin)> GetAffordableQuantity(
            Gainer stock,
            int requestedQuantity,
            decimal availableBalance)
        {
            if (requestedQuantity <= 0)
                return (0, 0);

            // Find the largest quantity up to the requested quantity that fits the
            // available balance. Required margin is expected to be monotonic by quantity,
            // so binary search avoids returning an unnecessarily low quantity after 20% steps.
            var low = 1;
            var high = requestedQuantity;
            var bestQuantity = 0;
            var bestMargin = 0m;

            while (low <= high)
            {
                var quantity = low + ((high - low) / 2);
                var margin = await _angel.GetRequiredMarginAsync(
                    stock.SymbolToken,
                    quantity,
                    stock.CurrentPrice,
                    stock.Exchange,
                    stock.ProductType,
                    "BUY");

                _logger.LogDebug("ORDER DIAGNOSTIC MarginCheck Symbol={Symbol} Token={Token} Quantity={Quantity} Price={Price:F4} Margin={Margin:F2} AvailableBalance={AvailableBalance:F2}", stock.Symbol, stock.SymbolToken, quantity, stock.CurrentPrice, margin, availableBalance);
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

            return $"<hr/><h3>EMA Crossover + Pullback Recovery Confirmation</h3>" +
                $"<p>Entry time (IST): {entryTimeIst:yyyy-MM-dd HH:mm:ss}</p>" +
                $"<p><b>EMA crossover</b><br/>EMA9: {ema9:F6}<br/>EMA21: {ema21:F6}<br/>EMA gap: {gapPercent:F6}%<br/>Crossover/signal candle time (IST): {(crossoverTime.HasValue ? crossoverTime.Value.ToString("yyyy-MM-dd HH:mm:ss") : "Unavailable")}</p>" +
                $"<p><b>Market structure / recovery</b><br/>Pullback low: {(pullbackLow > 0m ? pullbackLow.ToString("F6") : "Unavailable")}<br/>Recovery swing high broken: {(swingHigh > 0m ? swingHigh.ToString("F6") : "Unavailable")}<br/>Entry required a pullback, price recovery, and break above the minor swing high.</p>" +
                $"<p><b>Sideways-market filter</b><br/>Choppiness Index (14): {(chop >= 0m ? chop.ToString("F2") : "Unavailable")}<br/>Entry threshold: CHOP &lt; 61.8<br/>Result: {(chop >= 0m && chop < 61.8m ? "Passed" : "Not available in saved snapshot")}</p>" +
                $"<p><b>Stochastic RSI confirmation</b><br/>%K (RSI 14, Stoch 14, smoothing 3): {(stochK >= 0m ? stochK.ToString("F2") : "Unavailable")}<br/>%D (RSI 14, Stoch 14, smoothing 3): {(stochD >= 0m ? stochD.ToString("F2") : "Unavailable")}<br/>Entry required %K &gt; %D and rising %K.</p>";
        }

        private async Task<decimal> ExecuteTradeCandidate(
            TradeCandidate candidate,
            decimal remainingBalance)
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

            var capitalForTrade = RiskManagementService.CalculateCapitalForTrade(
                remainingBalance, config.MaxCapitalPerTradePercent);
            if (capitalForTrade <= 0m)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=CapitalForTrade Balance={Balance:F2} MaxCapitalPercent={MaxCapitalPercent} CalculatedCapital={Capital:F2}", stock.Symbol, remainingBalance, config.MaxCapitalPerTradePercent, capitalForTrade);
                stock.Suggestion = "Increase Max Capital Per Trade or add funds.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var atr = stock.ATR > 0m ? stock.ATR : stock.CurrentPrice * 0.005m;
            stock.StopLoss = TradeLevelCalculator.CalculateProtectiveStop(
                stock.CurrentPrice, atr, config.Equity.AtrStopMultiplier,
                config.Equity.MinimumStopPercent, config.Equity.MaximumStopPercent);
            if (stock.StopLoss <= 0m || stock.StopLoss >= stock.CurrentPrice)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=ProtectiveStop Price={Price:F4} ATR={ATR:F4} StopLoss={StopLoss:F4} MinStopPercent={MinStopPercent} MaxStopPercent={MaxStopPercent}", stock.Symbol, stock.CurrentPrice, atr, stock.StopLoss, config.Equity.MinimumStopPercent, config.Equity.MaximumStopPercent);
                stock.Suggestion = "Protective stop could not be calculated.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var availableRiskCapacity = await _intradayRiskGuard.GetAvailableRiskCapacityAsync();
            _logger.LogInformation("ORDER DIAGNOSTIC RiskCapacity Symbol={Symbol} AvailableRiskCapacity={AvailableRiskCapacity:F2} ConfiguredMaxOpenRisk={ConfiguredMaxOpenRisk:F2}", stock.Symbol, availableRiskCapacity, config.MaximumTotalOpenRisk);
            var quantityRiskCap = config.MaximumTotalOpenRisk > 0m
                ? Math.Min(config.MaximumTotalOpenRisk, availableRiskCapacity)
                : availableRiskCapacity;
            var useFixedQuantity = string.Equals(config.QuantityType, "Fixed", StringComparison.OrdinalIgnoreCase);
            int quantity;
            if (useFixedQuantity)
            {
                var matchingBand = config.FixedQuantityPriceBands?
                    .Where(x => x != null && x.StartPrice <= x.EndPrice && x.Quantity > 0)
                    .OrderBy(x => x.StartPrice)
                    .FirstOrDefault(x => stock.CurrentPrice >= x.StartPrice && stock.CurrentPrice <= x.EndPrice);

                quantity = matchingBand?.Quantity ?? 0;
                if (quantity <= 0)
                {
                    _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=FixedQuantityPriceBand Price={Price:F4} ConfiguredBands={BandCount}", stock.Symbol, stock.CurrentPrice, config.FixedQuantityPriceBands?.Count ?? 0);
                    stock.Suggestion = $"No valid fixed-quantity price band is configured for price ₹{stock.CurrentPrice:F2}.";
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }

                _logger.LogInformation("FIXED QUANTITY BAND Symbol={Symbol} Price={Price:F4} StartPrice={StartPrice:F4} EndPrice={EndPrice:F4} ConfiguredQuantity={ConfiguredQuantity}", stock.Symbol, stock.CurrentPrice, matchingBand!.StartPrice, matchingBand.EndPrice, quantity);

                // For a BUY, cap the configured quantity by the shares currently offered
                // for sale at the best ask price. For a SHORT entry, use bid-side depth.
                // This is market depth, not an affordability/margin calculation.
                var depth = GetAvailableQuantityAtExecutablePrice(stock, out var depthPrice, out var depthSide);
                if (depth <= 0)
                {
                    _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=MarketDepthUnavailable Side={DepthSide} ExecutablePrice={ExecutablePrice:F4} DepthPrice={DepthPrice:F4} ConfiguredQuantity={ConfiguredQuantity}", stock.Symbol, depthSide, GetExecutablePrice(stock), depthPrice, quantity);
                    stock.Suggestion = "Market depth quantity at the executable price is unavailable; fixed-quantity order was not placed.";
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }

                stock.FixedQuantityConfigured = quantity;
                stock.MarketDepthAvailableQuantity = depth;
                stock.MarketDepthExecutablePrice = depthPrice;
                stock.MarketDepthSide = depthSide;

                var depthCappedQuantity = (int)Math.Min(quantity, Math.Min(depth, int.MaxValue));
                if (depthCappedQuantity < quantity)
                {
                    _logger.LogInformation("FIXED QUANTITY CAPPED BY MARKET DEPTH Symbol={Symbol} Side={DepthSide} ExecutablePrice={ExecutablePrice:F4} AvailableAtPrice={AvailableAtPrice} ConfiguredQuantity={ConfiguredQuantity} FinalQuantity={FinalQuantity}", stock.Symbol, depthSide, depthPrice, depth, quantity, depthCappedQuantity);
                }
                quantity = depthCappedQuantity;
            }
            else
            {
                quantity = RiskManagementService.CalculateQuantity(
                    capitalForTrade, stock.CurrentPrice, stock.StopLoss, config.RiskPercentage,
                    stock.Direction, quantityRiskCap);
            }

            if (quantity <= 0)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=RiskQuantity Price={Price:F4} StopLoss={StopLoss:F4} Capital={Capital:F2} RiskPercent={RiskPercent} RiskCapacity={RiskCapacity:F2} Quantity={Quantity}", stock.Symbol, stock.CurrentPrice, stock.StopLoss, capitalForTrade, config.RiskPercentage, quantityRiskCap, quantity);
                stock.Suggestion = "Increase funds or reduce risk percentage.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var configuredQuantity = quantity;
            var affordable = await GetAffordableQuantity(stock, configuredQuantity, remainingBalance);
            // Market depth caps fixed quantity before this point. This separate check
            // continues to enforce the existing available-margin limit for both modes.
            quantity = Math.Min(configuredQuantity, affordable.Quantity);
            var requiredMargin = quantity > 0 ? affordable.Margin : 0m;
            if (quantity <= 0)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=AffordableQuantity ConfiguredQuantity={ConfiguredQuantity} AvailableBalance={Balance:F2} MarginReturned={Margin:F2}", stock.Symbol, configuredQuantity, remainingBalance, requiredMargin);
                stock.Suggestion = useFixedQuantity
                    ? $"No quantity from the configured fixed band is affordable at the current price with available margin ₹{remainingBalance:F2}."
                    : "Insufficient available margin for the calculated risk-based quantity.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            if (useFixedQuantity && quantity < configuredQuantity)
            {
                _logger.LogInformation("FIXED QUANTITY CAPPED BY AFFORDABILITY Symbol={Symbol} Price={Price:F4} ConfiguredQuantity={ConfiguredQuantity} AffordableQuantity={AffordableQuantity} FinalQuantity={FinalQuantity} AvailableBalance={AvailableBalance:F2} RequiredMargin={RequiredMargin:F2}", stock.Symbol, stock.CurrentPrice, configuredQuantity, affordable.Quantity, quantity, remainingBalance, requiredMargin);
            }

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
                return await HandlePaperTradeAsync(stock, candidate, quantity, requiredMargin, validation, stock, remainingBalance);
            }

            if (!config.EnableAutoTrading)
            {
                _logger.LogWarning("ORDER BLOCKED Symbol={Symbol} Gate=AutoTradingDisabled PaperTrading={PaperTrading} AutoTrading={AutoTrading}", stock.Symbol, config.PaperTrading, config.EnableAutoTrading);
                stock.Suggestion = "Auto trading is disabled.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            return await PlaceBuyOrderAsync(stock, candidate, quantity, requiredMargin, validation, stock, remainingBalance);
        }

        private async Task<decimal> HandlePaperTradeAsync(
            Gainer stock,
            TradeCandidate candidate,
            int quantity,
            decimal requiredMargin,
            TradeValidationResult validation,
            Gainer latest,
            decimal remainingBalance)
        {
            // Paper execution uses the same intraday marginal/executable quote:
            // long entries buy at Ask, short entries sell at Bid. The recorded
            // EntryPrice is this simulated fill price (not the signal/entry reference price).
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
                QuantityType = _tradingSettingsProvider.Current.QuantityType,
                FixedQuantityConfigured = stock.FixedQuantityConfigured,
                MarketDepthAvailableQuantity = stock.MarketDepthAvailableQuantity,
                MarketDepthExecutablePrice = stock.MarketDepthExecutablePrice,
                MarketDepthSide = stock.MarketDepthSide,
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
            _logger.LogInformation("PAPER ORDER CREATED Symbol={Symbol} TradeStatus={Status}", paperTrade.Symbol, paperTrade.Status);

            _watchList.MarkOwned(
                stock.SymbolToken,
                quantity,
                paperEntryPrice,
                paperStopLoss);

            try
            {
                var buyEmailBody = EmailHelper.BuildBuyOrderEmail(paperTrade, true)
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
                QuantityType = config.QuantityType,
                FixedQuantityConfigured = stock.FixedQuantityConfigured,
                MarketDepthAvailableQuantity = stock.MarketDepthAvailableQuantity,
                MarketDepthExecutablePrice = stock.MarketDepthExecutablePrice,
                MarketDepthSide = stock.MarketDepthSide,
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
