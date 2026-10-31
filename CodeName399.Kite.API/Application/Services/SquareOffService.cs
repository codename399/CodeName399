using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;

namespace CodeName399.Kite.API.Services;

public class SquareOffService
{
    private readonly IAngelOneService _angel;
    private readonly TradingSettingsProvider _settings;
    private readonly MarketClock _clock;
    private readonly ILogger<SquareOffService> _logger;

    public SquareOffService(
        IAngelOneService angel,
        TradingSettingsProvider settings,
        MarketClock clock,
        ILogger<SquareOffService> logger)
    {
        _angel = angel;
        _settings = settings;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Final market-close barrier: flatten every open intraday live/paper trade and
    /// expire every active active trade so autonomous testing cannot carry exposure
    /// or state into the next trading session.
    /// </summary>
    public async Task ExecuteAtMarketCloseAsync(CancellationToken token = default)
    {
        if (!_clock.IsTradingDay)
            return;

        token.ThrowIfCancellationRequested();

        try
        {
            await _sellTradingService.SquareOffPaperTradesAsync(forceAtMarketClose: true);

            var liveTrades = _tradeManager.GetOpen()
                .Where(x => x.ExecutionMode == TradeExecutionMode.Live && !x.Closed)
                .ToList();

            foreach (var trade in liveTrades)
            {
                token.ThrowIfCancellationRequested();

                var tradeSettings = _settings.Current.Equity;

                await ForceCloseLiveTradeAsync(
                    trade,
                    Math.Max(1, tradeSettings.MaximumExitRetries));
            }

            _logger.LogInformation(
                "MARKET CLOSE FINALIZATION COMPLETE. PaperTrades={PaperTrades}, LiveTrades={LiveTrades}",
                _tradeManager.GetOpen().Count(x => x.ExecutionMode == TradeExecutionMode.Paper && !x.Closed),
                _tradeManager.GetOpen().Count(x => x.ExecutionMode == TradeExecutionMode.Live && !x.Closed));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Market-close finalization failed.");
            throw;
        }
    }

    public async Task Execute()
    {
        if (!_clock.IsTradingDay)
            return;

        if (!_settings.Current.AutoSquareOff)
            return;

        try
        {
            await _sellTradingService.SquareOffPaperTradesAsync();

            var liveTrades = _tradeManager.GetOpen()
                .Where(x => x.ExecutionMode == TradeExecutionMode.Live)
                .ToList();

            foreach (var trade in liveTrades)
            {
                var squareOffTime = _clock.GetAutoSquareOffTime(_settings.Current, trade.ProductType);
                if (_clock.Now.TimeOfDay < squareOffTime)
                    continue;

                var tradeSettings = _settings.Current.Equity;

                await ForceCloseLiveTradeAsync(trade, Math.Max(1, tradeSettings.MaximumExitRetries));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Square-off failed.");
        }
    }

    private async Task ForceCloseLiveTradeAsync(TradeOrder trade, int maxAttempts)
    {
        for (var attempt = 1; attempt <= Math.Max(1, maxAttempts); attempt++)
        {
            if (trade.ExitOrderPlaced && !string.IsNullOrWhiteSpace(trade.ExitUniqueOrderId))
            {
                var detail = await _angel.GetIndividualOrderAsync(trade.ExitUniqueOrderId);
                if (detail == null)
                {
                    // An existing broker submission is unresolved. Never place a
                    // second exit while its state is unknown.
                    await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.Current.BrokerPositionConfirmationDelaySeconds)));
                    continue;
                }

                if (detail != null)
                {
                    var brokerStatus = string.IsNullOrWhiteSpace(detail.OrderStatus) ? detail.Status : detail.OrderStatus;
                    if (brokerStatus.Equals("COMPLETE", StringComparison.OrdinalIgnoreCase))
                    {
                        trade.ExitExecutionState = OrderExecutionState.Complete;
                        trade.ExitFilledQuantity = int.TryParse(detail.FilledShares, out var completeFilled) ? completeFilled : trade.ExitFilledQuantity;
                        trade.ExitAveragePrice = detail.AveragePrice > 0 ? detail.AveragePrice : trade.ExitAveragePrice;
                    }
                    else if (brokerStatus.Equals("REJECTED", StringComparison.OrdinalIgnoreCase)
                        || brokerStatus.StartsWith("CANCEL", StringComparison.OrdinalIgnoreCase))
                    {
                        trade.ExitExecutionState = brokerStatus.StartsWith("CANCEL", StringComparison.OrdinalIgnoreCase)
                            ? OrderExecutionState.Cancelled
                            : OrderExecutionState.Rejected;
                        trade.ExitOrderPlaced = false;
                        trade.ExitOrderId = string.Empty;
                        trade.ExitUniqueOrderId = string.Empty;
                        await _tradeManager.UpdateAsync(trade);
                    }
                    else
                    {
                        trade.ExitExecutionState = int.TryParse(detail.FilledShares, out var openFilled) && openFilled > 0
                            ? OrderExecutionState.PartiallyFilled
                            : OrderExecutionState.Open;
                        trade.ExitFilledQuantity = openFilled > 0 ? openFilled : trade.ExitFilledQuantity;
                        await _tradeManager.UpdateAsync(trade);
                        await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.Current.BrokerPositionConfirmationDelaySeconds)));
                        continue;
                    }
                }
            }

            var positions = await _angel.GetPositionsAsync(true);
            if (!_angel.LastPositionRefreshSucceeded)
            {
                _logger.LogWarning("Unable to verify broker position for {Symbol}; keeping local trade open.", trade.Symbol);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.Current.SquareOffRetryDelaySeconds)));
                continue;
            }

            var position = positions.FirstOrDefault(x =>
                string.Equals(x.Exchange, trade.Exchange, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.SymbolToken, trade.SymbolToken, StringComparison.OrdinalIgnoreCase));

            if (position == null || position.NetQty == 0)
            {
                trade.ExitRequired = false;
                trade.ExitOrderPlaced = false;
                trade.ExitExecutionState = OrderExecutionState.Complete;
                trade.ExitReason = string.IsNullOrWhiteSpace(trade.ExitReason) ? "Broker confirmed flat" : trade.ExitReason;
                if (trade.ExitPrice <= 0)
                    trade.ExitPrice = position?.Ltp > 0 ? position.Ltp : trade.CurrentPrice > 0 ? trade.CurrentPrice : trade.EntryPrice;
                await _tradeManager.CloseAsync(trade);
                _logger.LogInformation("Broker confirms {Symbol} ({Exchange}:{Token}) is flat; local trade closed.", trade.Symbol, trade.Exchange, trade.SymbolToken);
                return;
            }

            if (trade.ExitExecutionState == OrderExecutionState.Complete)
            {
                // Broker completed the previous exit order but the position is still
                // open (partial fill at the order level). Clear the old order identity
                // before creating a new exit for only the remaining broker quantity.
                trade.ExitOrderPlaced = false;
                trade.ExitOrderId = string.Empty;
                trade.ExitUniqueOrderId = string.Empty;
                await _tradeManager.UpdateAsync(trade);
            }

            var quantity = Math.Abs(position.NetQty);
            var direction = position.NetQty < 0 ? TradeDirection.Short : TradeDirection.Long;
            var transactionType = direction == TradeDirection.Short ? "BUY" : "SELL";
            trade.ExitOrderTag = string.IsNullOrWhiteSpace(trade.ExitOrderTag)
                ? $"CN399-SQ-{Guid.NewGuid():N}"[..19]
                : trade.ExitOrderTag;

            var response = await _angel.PlaceOrderAsync(
                new PlaceOrderRequest
                {
                    TradingSymbol = position.TradingSymbol,
                    SymbolToken = position.SymbolToken,
                    Exchange = position.Exchange,
                    TransactionType = transactionType,
                    ProductType = position.ProductType,
                    OrderType = "MARKET",
                    Variety = "NORMAL",
                    Duration = "DAY",
                    Quantity = quantity,
                    IsExitOrder = true,
                    OrderTag = trade.ExitOrderTag
                },
                forceLive: true);

            trade.ExitRequired = true;
            trade.ExitRetryCount = attempt;
            trade.ExitLastAttemptTime = DateTime.UtcNow;
            trade.ExitRequestedQuantity = quantity;
            trade.ExitOrderPlaced = response.Status;
            trade.ExitOrderPlacedAt = response.Status ? DateTime.UtcNow : trade.ExitOrderPlacedAt;
            trade.ExitOrderId = response.Data?.OrderId ?? trade.ExitOrderId;
            trade.ExitUniqueOrderId = response.Data?.UniqueOrderId ?? trade.ExitUniqueOrderId;
            trade.ExitExecutionState = response.Status ? OrderExecutionState.Submitted : OrderExecutionState.Submitted;
            await _tradeManager.UpdateAsync(trade);

            if (!response.Status)
            {
                _logger.LogWarning("Square-off attempt {Attempt}/{Max} failed for {Symbol}.", attempt, maxAttempts, trade.Symbol);
                await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.Current.SquareOffRetryDelaySeconds)));
                continue;
            }

            // Position endpoint is rate limited. Confirm the broker is flat before
            // allowing the local trade to be considered closed.
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, _settings.Current.BrokerPositionConfirmationDelaySeconds)));
            var after = await _angel.GetPositionsAsync(true);
            if (!_angel.LastPositionRefreshSucceeded)
            {
                _logger.LogWarning("Unable to confirm square-off result for {Symbol}; keeping local trade open.", trade.Symbol);
                continue;
            }

            var remaining = after.FirstOrDefault(x =>
                string.Equals(x.Exchange, trade.Exchange, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.SymbolToken, trade.SymbolToken, StringComparison.OrdinalIgnoreCase));

            if (remaining == null || remaining.NetQty == 0)
            {
                trade.ExitPrice = remaining?.Ltp > 0 ? remaining.Ltp : trade.ExitPrice;
                trade.ExitReason = "Intraday square-off";
                trade.ExitRequired = false;
                await _tradeManager.CloseAsync(trade);
                return;
            }

            _logger.LogWarning("Square-off order did not flatten {Symbol}; remaining quantity={Quantity}.", trade.Symbol, remaining.NetQty);
        }

        _logger.LogCritical("Unable to flatten live intraday position {Symbol} ({Exchange}:{Token}) after retries.", trade.Symbol, trade.Exchange, trade.SymbolToken);
    }
}
