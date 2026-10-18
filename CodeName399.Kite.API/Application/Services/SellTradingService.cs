using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.Models.WebSocketOrder;
using CodeName399.Kite.API.Services.Brokerage;
using CodeName399.Kite.API.Services.Interface;
using CodeName399.Kite.API.WebSocket.Interface;
using System.Text.Json;

namespace CodeName399.Kite.API.Services
{
    public class SellTradingService : TradingServiceBase
    {
        private readonly MarketClock _marketClock;
        private readonly IIndicatorService _indicatorService;

        public SellTradingService(
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
            MarketClock marketClock,
            IIndicatorService indicatorService,
            ILogger<SellTradingService> logger
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
            _marketClock = marketClock;
            _indicatorService = indicatorService;
            _orderWebSocket.OrderUpdated += OnOrderUpdated;
        }

        public async Task Execute(List<Gainer> gainers)
        {
            using var flow = TradingFlowActivity.Start("trading.sell-cycle");
            _logger.LogDebug("FLOW START SellCycle. CandidateCount={CandidateCount}, AutoTrading={AutoTrading}, PaperTrading={PaperTrading}, KillSwitch={KillSwitch}",
                gainers.Count, _tradingSettingsProvider.Current.EnableAutoTrading,
                _tradingSettingsProvider.Current.PaperTrading, _state.KillSwitch);

            if (!CanExecute())
                return;

            await ProcessSellOrders(gainers);

            _logger.LogDebug("FLOW END SellCycle. OpenTrades={OpenTrades}, PendingTrades={PendingTrades}",
                _tradeManager.GetOpen().Count, _tradeManager.GetPending().Count);
        }

        private async Task ProcessSellOrders(List<Gainer> gainers)
        {
            foreach (var stock in gainers.Where(x => x.IsOwned))
            {
                var trade = _tradeManager.GetByIdentity(stock.Exchange, stock.SymbolToken, stock.Symbol);
                if (trade == null)
                    continue;

                if (trade.Status != TradeStatus.Open)
                    continue;

                var instrumentSettings = _tradingSettingsProvider.Current.Equity;
                var maxTickAgeSeconds = Math.Max(1, instrumentSettings.MaxMarketDataAgeSeconds);
                if (stock.LastTickUpdated == default ||
                    DateTime.UtcNow - stock.LastTickUpdated > TimeSpan.FromSeconds(maxTickAgeSeconds))
                {
                    _logger.LogDebug(
                        "Skipping exit evaluation for {Symbol}; last tick is stale. LastTick={LastTick}",
                        trade.Symbol,
                        stock.LastTickUpdated);
                    continue;
                }

                if (trade.ExitOrderPlaced)
                {
                    if (!string.IsNullOrWhiteSpace(trade.ExitOrderId))
                    {
                        var brokerState = _orderCache.Get(trade.ExitOrderId);
                        if (brokerState != null
                            && (brokerState.ExecutionState == OrderExecutionState.Rejected
                                || brokerState.ExecutionState == OrderExecutionState.Cancelled))
                        {
                            _tradeManager.ResetExitPending(trade);
                            continue;
                        }
                    }

                    if (trade.ExitOrderPlacedAt.HasValue
                        && (DateTime.UtcNow - trade.ExitOrderPlacedAt.Value).TotalSeconds >= instrumentSettings.ExitOrderTimeoutSeconds)
                    {
                        if (trade.ExitRetryCount >= instrumentSettings.MaximumExitRetries)
                        {
                            _logger.LogError(
                                "Exit retry limit reached for {Symbol}. OrderId={OrderId} Position remains open.",
                                trade.Symbol,
                                trade.ExitOrderId);
                            continue;
                        }

                        if (!string.IsNullOrWhiteSpace(trade.ExitOrderId))
                        {
                            try
                            {
                                await _angel.CancelOrderAsync(trade.ExitOrderId);
                            }
                            catch (Exception cancelEx)
                            {
                                _logger.LogWarning(cancelEx, "Failed to cancel stale exit order {OrderId} for {Symbol}.", trade.ExitOrderId, trade.Symbol);
                            }
                        }

                        _tradeManager.ResetExitPending(trade);
                    }
                    else
                    {
                        continue;
                    }
                }

                try
                {
                    //----------------------------------------------------
                    // Trade-failure protection runs before profit trailing. A
                    // trade that never proves itself must not be allowed to
                    // become a multi-percent loser merely because its trailing
                    // stop has not activated.
                    //----------------------------------------------------

                    await UpdateTrailingOrders(stock, trade);

                    if (trade.Status != TradeStatus.Open)
                        continue;

                    // Automatic order placement is intentionally disabled.
                    // Exit decisions must be submitted through the explicit sell endpoint.
                    _logger.LogDebug(
                        "Automatic sell skipped for {Symbol}; use the manual sell endpoint.",
                        trade.Symbol);
                    continue;

                    trade.StopLossTriggered = false;
                    trade.StopLossTriggerPrice = 0;
                    trade.StopLossTriggerTime = null;

                    _tradeManager.MarkExitPending(trade);

                    //----------------------------------------------------
                    // Paper Trading
                    //----------------------------------------------------

                    if (trade.ExecutionMode == TradeExecutionMode.Paper)
                    {
                        trade.ExitPrice = trade.Direction == TradeDirection.Short ? (stock.Ask > 0 ? stock.Ask : stock.CurrentPrice) : (stock.Bid > 0 ? stock.Bid : stock.CurrentPrice);

                        trade.ExitTime = DateTime.UtcNow;

                        trade.ExitReason = "Manual sell";

                        trade.ExitIndicators = ResolveExitIndicators(trade, stock);
                        trade.ExitHistoricalContextJson = await CaptureHistoricalContextAsync(stock);
                        trade.ExitCategoryVersion = _tradingSettingsProvider.Current.ResearchDataVersion;
                        trade.ExitConfigurationSnapshotJson = TradingSettingsProvider.Serialize(_tradingSettingsProvider.Current);

                        await _tradeManager.CloseAsync(trade);

                        _watchList.MarkSold(trade.SymbolToken);

                        await UpdateTradingState(trade);

                        SendSellNotification(trade, paperTrade: true);
                        continue;
                    }

                    //----------------------------------------------------
                    // Auto Trading Disabled
                    //----------------------------------------------------

                    // Existing live trades continue to be managed until closed.
                    // A later global switch to EnableAutoTrading=false must not
                    // convert an already-live position into a paper-only trade.
                    if (trade.ExecutionMode != TradeExecutionMode.Live)
                    {
                        _state.RegisterBrokerFailure(_tradingSettingsProvider.Current);
                        trade.ExitRequired = true;
                        _tradeManager.ResetExitPending(trade);
                        continue;
                    }

                    //----------------------------------------------------
                    // Place SELL Order
                    //----------------------------------------------------

                    if (string.IsNullOrWhiteSpace(trade.ExitOrderTag))
                        trade.ExitOrderTag = $"CN399-EXIT-{Guid.NewGuid():N}"[..19];

                    var sell = await _angel.PlaceOrderAsync(
                        new PlaceOrderRequest
                        {
                            TradingSymbol = stock.Symbol,
                            SymbolToken = stock.SymbolToken,
                            Exchange = trade.Exchange,
                            TransactionType = trade.Direction == TradeDirection.Short ? "BUY" : "SELL",
                            ProductType = trade.ProductType,
                            OrderType = trade.OrderType,
                            Variety = "NORMAL",
                            Duration = trade.Duration,
                            Quantity = trade.Quantity,
                            IsExitOrder = true,
                            OrderTag = string.IsNullOrWhiteSpace(trade.ExitOrderTag) ? $"CN399-EXIT-{Guid.NewGuid():N}"[..19] : trade.ExitOrderTag,
                        }
                    );

                    if (!sell.Status)
                    {
                        _tradeManager.ResetExitPending(trade);
                        continue;
                    }

                    _state.RegisterBrokerSuccess();
                    _tradeManager.RefreshTrade(
                        trade.Exchange,
                        trade.SymbolToken,
                        t =>
                        {
                            t.ExitOrderId = sell.Data.OrderId;
                            t.ExitOrderPlaced = true;
                            t.ExitOrderPlacedAt = DateTime.UtcNow;
                            t.ExitLastAttemptTime = DateTime.UtcNow;
                            t.ExitRetryCount++;
                            t.ExitRequestedQuantity = t.Quantity;
                            t.ExitOrderTag = string.IsNullOrWhiteSpace(t.ExitOrderTag) ? $"CN399-EXIT-{Guid.NewGuid():N}"[..19] : t.ExitOrderTag;
                            t.ExitRequired = true;
                        }
                    );
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while selling {Symbol}", stock.Symbol);

                    _tradeManager.ResetExitPending(trade);
                }
            }
        }

        private async Task UpdateTrailingOrders(Gainer stock, TradeOrder trade)
        {
            if (trade.Status != TradeStatus.Open)
                return;

            //------------------------------------------------------
            // Update Highest / Lowest
            //------------------------------------------------------

            if (stock.CurrentPrice > trade.HighestPrice)
                trade.HighestPrice = stock.CurrentPrice;

            if (trade.LowestPrice <= 0)
                trade.LowestPrice = trade.EntryPrice;

            if (stock.CurrentPrice < trade.LowestPrice)
                trade.LowestPrice = stock.CurrentPrice;

            decimal atr = trade.ATR > 0 ? trade.ATR : stock.ATR;

            // Keep trailing functional even when a restarted/recovered trade has
            // temporarily missing ATR data. This matches the entry-side fallback.
            if (atr <= 0 && trade.EntryPrice > 0)
                atr = trade.EntryPrice * 0.005m;

            if (atr <= 0)
                return;

            //------------------------------------------------------
            // Calculate current executable-side net profit.
            //
            // Long  -> Bid is the conservative exit price.
            // Short -> Ask is the conservative exit price.
            // Fall back to LTP when Bid/Ask is unavailable.
            //------------------------------------------------------

            decimal executableExitPrice = trade.Direction == TradeDirection.Short
                ? (stock.Ask > 0 ? stock.Ask : stock.CurrentPrice)
                : (stock.Bid > 0 ? stock.Bid : stock.CurrentPrice);

            if (executableExitPrice <= 0)
                return;

            decimal estimatedNetProfit = _tradeManager.CalculateEstimatedNetProfit(
                trade,
                executableExitPrice);

            trade.EstimatedNetProfit = estimatedNetProfit;

            // Track the best executable-side net profit seen for this stock.
            // This is intentionally per-trade/per-stock, not a portfolio-wide value.
            if (estimatedNetProfit > trade.PeakEstimatedNetProfit)
                trade.PeakEstimatedNetProfit = estimatedNetProfit;

            //------------------------------------------------------
            // Break-even protection comes first. Once the executable-side
            // position is net profitable after estimated charges, move the
            // protective stop to the charge-aware break-even price.
            // This is the only step between the original protective stop and
            // the trailing protection stage.
            //------------------------------------------------------
            if (!trade.TrailingActivated && estimatedNetProfit > 0m && trade.Quantity > 0)
            {
                var breakEven = trade.MinimumExitPrice > 0m
                    ? trade.MinimumExitPrice
                    : trade.BreakEvenPrice;

                var breakEvenIsValid = trade.Direction == TradeDirection.Short
                    ? breakEven > 0m && breakEven < executableExitPrice && breakEven < trade.StopLoss
                    : breakEven > trade.StopLoss && breakEven < executableExitPrice;

                if (breakEvenIsValid)
                {
                    trade.StopLoss = trade.Direction == TradeDirection.Short
                        ? OrderPriceValidator.RoundUp(breakEven)
                        : OrderPriceValidator.RoundDown(breakEven);
                    trade.TrailingProtectedProfit = 0m;
                    await _tradeManager.UpdateAsync(trade);

                    _logger.LogDebug(
                        "Break-even protection activated for {Symbol}. NetProfit={NetProfit:F2}, BreakEven={BreakEven:F2}",
                        trade.Symbol, estimatedNetProfit, trade.StopLoss);
                }
            }

            //------------------------------------------------------
            // Activate trailing ONLY after net profit crosses the
            // configured threshold. ATR/profit-percent movement is
            // deliberately NOT an activation condition.
            //------------------------------------------------------

            if (!trade.TrailingActivated)
            {
                // Trailing starts as soon as the trade is net profitable after
                // charges. No additional trend, RSI, volume, ATR-move, or
                // strategy-specific activation gate is used.
                if (estimatedNetProfit <= 0m)
                    return;

                trade.TrailingActivated = true;
                trade.TrailingActivatedTime = DateTime.UtcNow;
                await _tradeManager.UpdateAsync(trade);

                _logger.LogInformation(
                    "Trailing protection activated for {Symbol}. NetProfit={NetProfit:F2}, Entry={Entry:F2}, ExitReference={ExitReference:F2}",
                    trade.Symbol, estimatedNetProfit, trade.EntryPrice, executableExitPrice);
            }

            //------------------------------------------------------
            // New Stop
            //
            // Trailing is a ratchet. It may move only in the direction
            // that protects more profit. The ATR trail follows price,
            // while the profit-lock floor follows the best net profit
            // achieved by THIS stock/trade.
            //------------------------------------------------------

            decimal trailingAtrMultiplier = _tradingSettingsProvider.Current.Exit.TrailingStopAtrMultiplier;

            decimal newStop = trade.Direction == TradeDirection.Short
                ? trade.LowestPrice + (atr * trailingAtrMultiplier)
                : trade.HighestPrice - (atr * trailingAtrMultiplier);

            decimal protectedPrice =
                trade.MinimumExitPrice > 0 ? trade.MinimumExitPrice
                : trade.BreakEvenPrice > 0 ? trade.BreakEvenPrice
                : trade.EntryPrice;

            // Convert the peak net profit into a stop price that retains a
            // configurable portion of that profit after estimated charges.
            // This is what prevents a profitable stock from giving its gains
            // back as the market retraces.
            decimal retentionPercent = Math.Clamp(
                _tradingSettingsProvider.Current.Exit.TrailingProfitRetentionPercent,
                0m,
                100m);

            decimal peakNetProfit = Math.Max(0m, trade.PeakEstimatedNetProfit);
            decimal retainedNetProfit = peakNetProfit * retentionPercent / 100m;
            decimal retainedGrossProfit = retainedNetProfit + Math.Max(0m, trade.EstimatedCharges);

            if (trade.Quantity > 0)
            {
                decimal profitLockStop = trade.Direction == TradeDirection.Short
                    ? trade.EntryPrice - (retainedGrossProfit / trade.Quantity)
                    : trade.EntryPrice + (retainedGrossProfit / trade.Quantity);

                if (trade.Direction == TradeDirection.Short)
                    newStop = Math.Min(newStop, profitLockStop);
                else
                    newStop = Math.Max(newStop, profitLockStop);
            }

            // Never loosen an already submitted/protected stop.
            if (trade.Direction == TradeDirection.Short)
            {
                newStop = Math.Min(newStop, protectedPrice);
                newStop = OrderPriceValidator.RoundUp(newStop);

                if (trade.StopLoss > 0)
                    newStop = Math.Min(newStop, trade.StopLoss);

                if (newStop >= trade.StopLoss)
                    return;
            }
            else
            {
                newStop = Math.Max(newStop, protectedPrice);
                newStop = OrderPriceValidator.RoundDown(newStop);

                if (trade.StopLoss > 0)
                    newStop = Math.Max(newStop, trade.StopLoss);

                if (newStop <= trade.StopLoss)
                    return;
            }

            _logger.LogDebug(
                "Trailing ratchet {Symbol}: currentNet={CurrentNet:F2}, peakNet={PeakNet:F2}, retention={Retention:F1}%, newStop={NewStop:F2}, atrStop={AtrStop:F2}",
                trade.Symbol, estimatedNetProfit, peakNetProfit, retentionPercent, newStop,
                trade.Direction == TradeDirection.Short
                    ? trade.LowestPrice + (atr * _tradingSettingsProvider.Current.Exit.TrailingStopAtrMultiplier)
                    : trade.HighestPrice - (atr * _tradingSettingsProvider.Current.Exit.TrailingStopAtrMultiplier));

            _tradeManager.UpdateTrailing(trade, trade.Direction == TradeDirection.Short ? trade.LowestPrice : trade.HighestPrice, newStop);

            await _tradeManager.UpdateAsync(trade);
        }

        protected override async Task SyncTradeOrder(OrderStatusMessage order)
        {
            using var flow = TradingFlowActivity.Start(
                "trading.sell-order-update", order.Symbol, order.SymbolToken,
                direction: "EXIT");

            _logger.LogDebug("FLOW STAGE Sell.OrderUpdate Symbol={Symbol}, Token={Token}, OrderId={OrderId}, Transaction={Transaction}, Status={Status}, ExecutionState={ExecutionState}, Filled={Filled}/{Quantity}, AveragePrice={AveragePrice}",
                order.Symbol, order.SymbolToken, order.OrderId, order.TransactionType, order.Status,
                order.ExecutionState, order.FilledQuantity, order.Quantity, order.AveragePrice);

            try
            {
                if (!string.Equals(order.TransactionType, "SELL", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(order.TransactionType, "BUY", StringComparison.OrdinalIgnoreCase))
                    return;

                var trade = _tradeManager.GetByIdentity(order.Exchange, order.SymbolToken, order.Symbol);
                if (trade == null)
                    return;

                var expectedExitTransaction =
                    trade.Direction == TradeDirection.Short ? "BUY" : "SELL";

                if (!string.Equals(order.TransactionType, expectedExitTransaction, StringComparison.OrdinalIgnoreCase))
                    return;

                if (trade.Closed)
                    return;

                if (order.ExecutionState == OrderExecutionState.PartiallyFilled)
                {
                    await _tradeManager.UpdatePartialExitAsync(
                        trade,
                        order.OrderId,
                        order.Quantity,
                        order.FilledQuantity,
                        order.AveragePrice,
                        order.ExecutionState,
                        order.UpdatedTime);
                    return;
                }

                if (order.ExecutionState == OrderExecutionState.Cancelled
                    || order.ExecutionState == OrderExecutionState.Rejected)
                {
                    var filled = order.FilledQuantity;
                    if (filled > 0 && filled < trade.Quantity)
                    {
                        trade.Quantity -= filled;
                        trade.ExitFilledQuantity = 0;
                        trade.ExitAveragePrice = 0;
                    }

                    _tradeManager.ResetExitPending(trade);
                    await _tradeManager.UpdateAsync(trade);
                    return;
                }

                if (!string.Equals(order.Status, "COMPLETE", StringComparison.OrdinalIgnoreCase))
                    return;

                if (trade.StopLossOrderId == order.OrderId)
                    trade.ExitReason = "Stop Loss";
                else if (string.IsNullOrWhiteSpace(trade.ExitReason))
                    trade.ExitReason = "Protective/Trailing Exit";

                var latest = _watchList.Get(trade.SymbolToken);

                if (latest != null)
                {
                    trade.ExitIndicators = ResolveExitIndicators(trade, latest);
                    trade.ExitHistoricalContextJson = await CaptureHistoricalContextAsync(latest);
                }
                trade.ExitCategoryVersion = _tradingSettingsProvider.Current.ResearchDataVersion;
                trade.ExitConfigurationSnapshotJson = TradingSettingsProvider.Serialize(_tradingSettingsProvider.Current);

                bool closed = await _tradeManager.ConfirmSell(
                    trade.Symbol,
                    order.AveragePrice,
                    order.UpdatedTime,
                    order.OrderId,
                    order.FilledQuantity
                );

                if (!closed)
                    return;

                _watchList.MarkSold(trade.SymbolToken);

                await UpdateTradingState(trade);

                decimal grossProfit = (trade.Direction == TradeDirection.Short
                            ? trade.EntryPrice - trade.ExitPrice
                            : trade.ExitPrice - trade.EntryPrice) * trade.Quantity;

                decimal netProfit = grossProfit - trade.EstimatedCharges;

                try
                {
                    SendSellNotification(trade, paperTrade: false);
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Exit notification failed for {Symbol}; trade remains closed.", trade.Symbol);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SELL websocket failed.");
            }
        }

        private bool SendSellNotification(TradeOrder trade, bool paperTrade)
        {
            if (!_tradingSettingsProvider.Current.EnableNotification)
            {
                _logger.LogInformation("SELL notification skipped because EnableNotification=false. Symbol={Symbol}", trade.Symbol);
                return false;
            }

            var body = EmailHelper.BuildSellOrderEmail(trade, paperTrade);
            var subject = $"{(paperTrade ? "PAPER " : string.Empty)}SELL - {trade.Symbol}";
            var sent = EmailHelper.SendEmail(body, _tradingSettingsProvider.Current, subject, true);
            if (!sent)
                _logger.LogWarning("SELL notification was not delivered for {Symbol}.", trade.Symbol);
            return sent;
        }

        private static TradeIndicatorSnapshot? ResolveExitIndicators(TradeOrder trade, Gainer? watch)
        {
            if (watch != null && watch.ATR > 0m)
                return IndicatorMapper.ToTradeIndicatorSnapshot(watch);
            return trade.EntryIndicators;
        }

        private async Task<string> CaptureHistoricalContextAsync(Gainer stock)
        {
            try
            {
                if (stock.Quote == null) return "";
                var snapshot = await _indicatorService.GetAsync(stock.Quote, string.Empty);
                if (snapshot == null) return "";
                var payload = new
                {
                    capturedAtUtc = DateTime.UtcNow,
                    oneMinute = snapshot.Candles1m.TakeLast(60).ToList(),
                    fiveMinute = snapshot.Candles5m.TakeLast(30).ToList()
                };
                return JsonSerializer.Serialize(payload);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Unable to capture exit historical research context for {Symbol}.", stock.Symbol);
                return "";
            }
        }

        private Task UpdateTradingState(TradeOrder trade)
        {
            if (trade.StateUpdated)
                return Task.CompletedTask;

            _tradeManager.RefreshTrade(trade.Exchange, trade.SymbolToken, t => t.StateUpdated = true);

            _state.RegisterTradeResult(
                (trade.Direction == TradeDirection.Short
                    ? trade.EntryPrice - trade.ExitPrice
                    : trade.ExitPrice - trade.EntryPrice) * trade.Quantity,
                _tradingSettingsProvider.Current
            );

            return Task.CompletedTask;
        }

        public async Task SquareOffPaperTradesAsync(bool forceAtMarketClose = false)
        {
            foreach (var trade in _tradeManager.GetOpenTrades().Where(x => x.ExecutionMode == TradeExecutionMode.Paper))
            {
                var squareOffTime = _marketClock.GetAutoSquareOffTime(_tradingSettingsProvider.Current, trade.ProductType);

                if (!forceAtMarketClose && _marketClock.Now.TimeOfDay < squareOffTime)
                    continue;

                var watch = _watchList.Get(trade.SymbolToken);

                decimal exitPrice = watch == null
                    ? trade.EntryPrice
                    : trade.Direction == TradeDirection.Short
                        ? (watch.Ask > 0 ? watch.Ask : watch.CurrentPrice)
                        : (watch.Bid > 0 ? watch.Bid : watch.CurrentPrice);

                trade.ExitPrice = exitPrice;
                trade.ExitTime = DateTime.UtcNow;
                trade.ExitReason = "Market Close";
                trade.ExitCategory = "SESSION";
                trade.ExitTrigger = "MARKET_CLOSE";
                trade.ExitCategoryVersion = _tradingSettingsProvider.Current.ResearchDataVersion;
                trade.ExitConfigurationSnapshotJson = TradingSettingsProvider.Serialize(_tradingSettingsProvider.Current);
                trade.ExitTriggerPrice = exitPrice;

                trade.ExitIndicators = ResolveExitIndicators(trade, watch);


                await _tradeManager.CloseAsync(trade);

                _watchList.MarkSold(trade.SymbolToken);

                await UpdateTradingState(trade);

                try
                {
                    SendSellNotification(trade, paperTrade: true);
                }
                catch (Exception emailEx)
                {
                    _logger.LogWarning(emailEx, "Paper exit notification failed for {Symbol}; trade remains closed.", trade.Symbol);
                }
            }
        }
    }
}
