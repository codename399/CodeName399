using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Helpers.Calculators;
using CodeName399.Kite.API.Prediction;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
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
        private readonly ExpectedPredictionService _prediction;
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
            ExpectedPredictionService prediction,
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
            _prediction = prediction;
        }

        public async Task Execute()
        {
            var gainers = _watchList.GetBuyCandidates();

            if (!CanExecute())
                return;

            if (!_state.CanTrade(_tradingSettingsProvider.Current))
                return;

            // All entries obey the broker session and intraday cutoff.
            if (!_tradingSettingsProvider.Current.IgnoreMarketHours && !_marketClock.IsNewIntradayEntryWindowOpen)
                return;

            string jwtToken = (await _angel.GetLoginDataAsync()).JwtToken;

            await RefreshSubscribedStocksAsync(jwtToken);

            decimal remainingBalance = await GetAvailableBalanceAsync();

            if (remainingBalance <= 0)
            {
                return;
            }

            await ProcessBuyOrders(gainers.ToList(), remainingBalance);
        }

        private async Task<decimal> ProcessStock(Gainer stock, decimal remainingBalance)
        {
            if (stock.IsOwned) { _tradeManager.ReleaseSymbol(stock.Symbol); stock.Suggestion = "Already owned."; return remainingBalance; }
            if (!_tradingSettingsProvider.Current.IgnoreMarketHours && !_marketClock.IsNewIntradayEntryWindowOpen) { stock.Suggestion = "New entry window is closed."; _tradeManager.ReleaseSymbol(stock.Symbol); return remainingBalance; }
            if (stock.InstrumentType != InstrumentType.Equity) { stock.Suggestion = "Only equity BUY candidates are supported."; _tradeManager.ReleaseSymbol(stock.Symbol); return remainingBalance; }
            try
            {
                if (_tradeManager.IsOpen(stock.Symbol)) { stock.Suggestion = "Close the existing position before buying again."; _tradeManager.ReleaseSymbol(stock.Symbol); return remainingBalance; }
                if (!_prediction.IsEnabled)
                {
                    stock.Suggestion = "Prediction trading is disabled in TradingConfiguration.Prediction.Enabled.";
                    _logger.LogInformation("BUY REJECTED Symbol={Symbol} Reason={Reason}", stock.Symbol, stock.Suggestion);
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }
                if (!_prediction.IsModelReady)
                {
                    stock.Suggestion = $"Prediction model is not ready. ModelPath={_prediction.ModelFilePath}";
                    _logger.LogWarning("BUY REJECTED Symbol={Symbol} Reason={Reason} ModelReady={ModelReady} ModelPath={ModelPath}", stock.Symbol, stock.Suggestion, _prediction.IsModelReady, _prediction.ModelFilePath);
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }

                var currentTick = ExpectedPredictionService.ToMarketTick(stock);
                if (!_prediction.TryGetLiveHistory(stock.SymbolToken, out var predictionHistory))
                {
                    stock.Suggestion = $"Prediction history is not ready; need {_prediction.Options.MinimumHistoryTicks} ticks.";
                    _logger.LogDebug("BUY REJECTED Symbol={Symbol} Reason={Reason}", stock.Symbol, stock.Suggestion);
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }

                var predictionDecision = _prediction.EvaluateEntry(currentTick, predictionHistory);
                stock.Suggestion = $"Prediction {predictionDecision.Probability:F3}";
                _logger.LogInformation("PREDICTION ENTRY Symbol={Symbol} Probability={Probability:F4} Decision={Decision} Reason={Reason}", stock.Symbol, predictionDecision.Probability, predictionDecision.ShouldBuy ? "BUY" : "HOLD", predictionDecision.Reason);
                stock.Reason = predictionDecision.Reason;
                if (!predictionDecision.ShouldBuy)
                {
                    _tradeManager.ReleaseSymbol(stock.Symbol);
                    return remainingBalance;
                }
                var candidate = new TradeCandidate
{
    Stock = stock,
    EntryDecision = "PREDICTION_BUY",
    EntryPredictionProbability = predictionDecision.Probability,
    EntryPredictionTimestampUtc = currentTick.ExchangeTime,
    PredictionModelVersion = _prediction.ModelVersion,
    SignalPrice = stock.CurrentPrice,
    SignalAtr = stock.ATR,
    SignalTimeUtc = currentTick.ExchangeTime,
    BuyEligibilityReason = $"PREDICTION_BUY:{predictionDecision.Probability:F3}"
};
                return await ExecuteTradeCandidate(candidate, remainingBalance);
            }
            catch (Exception ex) { _logger.LogError(ex, "Failed processing BUY for {Symbol}.", stock.Symbol); _tradeManager.ReleaseSymbol(stock.Symbol); return remainingBalance; }
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

        private async Task<(int Quantity, decimal Margin)> GetAffordableQuantity(
            Gainer stock,
            int requestedQuantity,
            decimal availableBalance)
        {
            if (requestedQuantity <= 0)
                return (0, 0);

            var quantity = requestedQuantity;

            while (quantity > 0)
            {
                var margin = await _angel.GetRequiredMarginAsync(
                    stock.SymbolToken,
                    quantity,
                    stock.CurrentPrice,
                    stock.Exchange,
                    stock.ProductType,
                    "BUY");

                if (margin > 0 && margin <= availableBalance)
                    return (quantity, margin);

                quantity = margin <= 0
                    ? quantity - 1
                    : Math.Min(quantity - 1, Math.Max(1, (int)Math.Floor(quantity * 0.8m)));
            }

            return (0, 0);
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
            var stopLoss = trade.StopLoss > 0 ? trade.StopLoss : OrderPriceValidator.RoundDown(calculatedStop);
            var chargesPerUnit = quantity > 0 ? trade.EstimatedCharges / quantity : 0m;
            var breakEven = trade.Direction == TradeDirection.Short
                ? OrderPriceValidator.RoundDown(entryPrice - chargesPerUnit)
                : OrderPriceValidator.RoundUp(entryPrice + chargesPerUnit);

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
                    _tradingSettingsProvider.Current.PaperTrading);
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

        private async Task<decimal> ExecuteTradeCandidate(
            TradeCandidate candidate,
            decimal remainingBalance)
        {
            var stock = candidate.Stock;
            var config = _tradingSettingsProvider.Current;

            if (stock.CurrentPrice <= 0m)
            {
                stock.Suggestion = "Invalid market price.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var capitalForTrade = RiskManagementService.CalculateCapitalForTrade(
                remainingBalance, config.MaxCapitalPerTradePercent);
            if (capitalForTrade <= 0m)
            {
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
                stock.Suggestion = "Protective stop could not be calculated.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var availableRiskCapacity = await _intradayRiskGuard.GetAvailableRiskCapacityAsync();
            var quantityRiskCap = config.MaximumTotalOpenRisk > 0m
                ? Math.Min(config.MaximumTotalOpenRisk, availableRiskCapacity)
                : availableRiskCapacity;
            var quantity = RiskManagementService.CalculateQuantity(
                capitalForTrade, stock.CurrentPrice, stock.StopLoss, config.RiskPercentage,
                stock.Direction, quantityRiskCap);
            if (quantity <= 0)
            {
                stock.Suggestion = "Increase funds or reduce risk percentage.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var affordable = await GetAffordableQuantity(stock, quantity, remainingBalance);
            quantity = affordable.Quantity;
            var requiredMargin = affordable.Margin;
            if (quantity <= 0)
            {
                stock.Suggestion = "Insufficient available margin for the calculated risk-based quantity.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            var validation = await _tradeValidation.ValidateAsync(stock, quantity);
            if (!validation.CanTrade)
            {
                stock.Suggestion = validation.Suggestion;
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            // The only entry validation remaining is charge-inclusive monetary risk.
            var estimatedRisk = Math.Max(0m, validation.NetRisk);
            var maximumTotalOpenRisk = config.MaximumTotalOpenRisk;
            if (maximumTotalOpenRisk > 0m && estimatedRisk > maximumTotalOpenRisk)
            {
                stock.Suggestion = $"Hard monetary risk cap exceeded. Net risk ₹{estimatedRisk:F2} > cap ₹{maximumTotalOpenRisk:F2}.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            if (!await _intradayRiskGuard.TryReserveAsync(stock, estimatedRisk))
            {
                stock.Suggestion = $"Hard monetary risk cap reached. Charge-inclusive risk ₹{estimatedRisk:F2}.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            candidate.Stock = stock;
            candidate.EntryIndicators = IndicatorMapper.ToTradeIndicatorSnapshot(stock);
            candidate.BuyEligibilityReason = candidate.BuyEligibilityReason.StartsWith("PREDICTION_BUY", StringComparison.OrdinalIgnoreCase)
                ? candidate.BuyEligibilityReason
                : "PREDICTION_BUY";
            candidate.EligibilityReason = candidate.BuyEligibilityReason;

            if (config.PaperTrading)
                return await HandlePaperTradeAsync(stock, candidate, quantity, requiredMargin, validation, stock, remainingBalance);

            if (!config.EnableAutoTrading)
            {
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
                EntryPrice = stock.CurrentPrice,
                EntryTime = DateTime.UtcNow,
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
                BreakEvenPrice = stock.Direction == TradeDirection.Short
                    ? OrderPriceValidator.RoundDown(stock.CurrentPrice - (validation.Charges.TotalCharges / quantity))
                    : OrderPriceValidator.RoundUp(stock.CurrentPrice + (validation.Charges.TotalCharges / quantity)),
                MinimumExitPrice = stock.Direction == TradeDirection.Short
                    ? OrderPriceValidator.RoundDown(stock.CurrentPrice - (validation.Charges.TotalCharges / quantity))
                    : OrderPriceValidator.RoundUp(stock.CurrentPrice + (validation.Charges.TotalCharges / quantity)),
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
                EntryPredictionProbability = candidate.EntryPredictionProbability,
                EntryPredictionTimestampUtc = candidate.EntryPredictionTimestampUtc,
                PredictionModelVersion = candidate.PredictionModelVersion,
            };

            IndicatorMapper.ApplyTradeIndicators(paperTrade, latest, captureEntrySnapshot: true);

            if (_tradeManager.Contains(paperTrade.Symbol))
            {
                stock.Suggestion = "Trade already pending.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            _tradeManager.GetOrAdd(paperTrade);
            await _tradeManager.SaveAsync(paperTrade);

            _watchList.MarkOwned(
                stock.SymbolToken,
                quantity,
                stock.CurrentPrice,
                stock.StopLoss);

            try
            {
                var buyEmailBody = EmailHelper.BuildBuyOrderEmail(paperTrade, true);
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
                EntryPredictionProbability = candidate.EntryPredictionProbability,
                EntryPredictionTimestampUtc = candidate.EntryPredictionTimestampUtc,
                PredictionModelVersion = candidate.PredictionModelVersion,
                EligibilityReason = candidate.BuyEligibilityReason
            };

            IndicatorMapper.ApplyTradeIndicators(tradeOrder, latest, captureEntrySnapshot: true);

            if (_tradeManager.Contains(tradeOrder.Symbol))
            {
                stock.Suggestion = "Trade already pending.";
                _tradeManager.ReleaseSymbol(stock.Symbol);
                return remainingBalance;
            }

            if (!_tradeManager.TryCreatePendingTrade(tradeOrder, out _))
            {
                stock.Suggestion = "Trade already active.";
                return remainingBalance;
            }

            var buy = await _angel.PlaceOrderAsync(
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

            if (!buy.Status)
            {
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
                return await _paperPortfolio.GetAvailableBalanceAsync();

            if (DateTime.UtcNow - _lastBalanceRefresh > TimeSpan.FromSeconds(30))
            {
                _cachedBalance = await _angel.GetAvailableBalanceAsync();
                _lastBalanceRefresh = DateTime.UtcNow;
            }

            return _cachedBalance;
        }

        private async Task RefreshSubscribedStocksAsync(string jwtToken)
        {
            foreach (var stock in _watchList.Current)
            {
                if (!stock.IsSubscribed || stock.IsOwned || stock.Quote == null)
                    continue;

                var indicator = await _indicatorService.GetAsync(stock.Quote, jwtToken);
                if (indicator == null)
                    continue;

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
