using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Services;

public class BrokerReconciliationService
{
    private readonly IAngelOneService _angel;
    private readonly TradeManager _tradeManager;
    private readonly WatchListService _watchList;
    private readonly ILogger<BrokerReconciliationService> _logger;
    private readonly TradingSettingsProvider _tradingSettingsProvider;
    private readonly SymbolLockService _symbolLocks;
    private readonly ITradeOrderFileRepository _repository;
    private readonly IInstrumentService _instrumentService;

    public BrokerReconciliationService(
        IAngelOneService angel,
        TradingSettingsProvider tradingSettingsProvider,
        TradeManager tradeManager,
        WatchListService watchList,
        SymbolLockService symbolLocks,
        ITradeOrderFileRepository repository,
        IInstrumentService instrumentService,
        ILogger<BrokerReconciliationService> logger
    )
    {
        _angel = angel;
        _tradingSettingsProvider = tradingSettingsProvider;
        _tradeManager = tradeManager;
        _watchList = watchList;
        _symbolLocks = symbolLocks;
        _repository = repository;
        _instrumentService = instrumentService;
        _logger = logger;
    }

    public async Task ReconcileAsync()
    {
        if (_tradingSettingsProvider.Current.PaperTrading
            && !_tradeManager.GetOpen().Any(x => x.ExecutionMode == TradeExecutionMode.Live))
        {
            return;
        }

        var positions = await _angel.GetPositionsAsync(true);
        if (!_angel.LastPositionRefreshSucceeded)
        {
            _logger.LogWarning("Broker reconciliation skipped because the position refresh failed.");
            return;
        }

        var brokerPositions = positions
            .Where(x => x.NetQty != 0)
            .GroupBy(PositionIdentity.Create, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var openTrades = _tradeManager.GetOpen()
            .Where(x => x.ExecutionMode == TradeExecutionMode.Live)
            .ToList();

        //------------------------------------------------------
        // Reconcile existing trades
        //------------------------------------------------------

        foreach (var trade in openTrades)
        {
            // Don't interfere while exit order is pending.
            if (trade.ExitOrderPlaced || trade.Status == TradeStatus.PendingBrokerConfirmation)
            {
                continue;
            }

            //--------------------------------------------------
            // Broker no longer has this position
            //--------------------------------------------------

            var tradePositionKey = PositionIdentity.Create(trade);

            if (!brokerPositions.TryGetValue(tradePositionKey, out var brokerPosition))
            {
                var orders = await _angel.GetOrderBookAsync();
                var expectedExit = trade.Direction == TradeDirection.Short ? "BUY" : "SELL";
                var confirmedExit = orders
                    .Where(x => trade.InstrumentType == InstrumentType.Equity
                        ? (string.IsNullOrWhiteSpace(x.SymbolToken) || string.Equals(x.SymbolToken, trade.SymbolToken, StringComparison.OrdinalIgnoreCase))
                        : string.Equals(x.SymbolToken, trade.SymbolToken, StringComparison.OrdinalIgnoreCase))
                    .Where(x => trade.InstrumentType == InstrumentType.Equity
                        ? (string.IsNullOrWhiteSpace(x.Exchange) || string.Equals(x.Exchange, trade.Exchange, StringComparison.OrdinalIgnoreCase))
                        : string.Equals(x.Exchange, trade.Exchange, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.TransactionType, expectedExit, StringComparison.OrdinalIgnoreCase))
                    .Where(x => string.Equals(x.OrderStatus, "COMPLETE", StringComparison.OrdinalIgnoreCase))
                    .Where(x => x.FilledQuantity > 0)
                    .OrderByDescending(x => x.UpdateTime)
                    .FirstOrDefault();

                if (confirmedExit != null)
                {
                    trade.ExitPrice = confirmedExit.AveragePrice > 0
                        ? confirmedExit.AveragePrice
                        : (trade.Direction == TradeDirection.Short
                            ? (trade.LowestPrice > 0 ? trade.LowestPrice : trade.EntryPrice)
                            : (trade.HighestPrice > 0 ? trade.HighestPrice : trade.EntryPrice));
                    trade.ExitTime = DateTime.TryParse(confirmedExit.UpdateTime, out var exitTime)
                        ? exitTime
                        : DateTime.UtcNow;
                    trade.ExitReason = "Broker Confirmed Exit";
                    trade.ExitFilledQuantity = confirmedExit.FilledQuantity;
                    trade.ExitAveragePrice = trade.ExitPrice;
                    await _tradeManager.CloseAsync(trade);
                    if (trade.InstrumentType == InstrumentType.Equity) _symbolLocks.Release(trade.Symbol);
                    else _symbolLocks.ReleasePosition(trade.Exchange, trade.SymbolToken);
                    if (!string.IsNullOrWhiteSpace(trade.SymbolToken))
                        _watchList.MarkSold(trade.SymbolToken);
                    _logger.LogInformation("{Symbol} closed locally after broker order-book confirmation. ExitOrder={OrderId}", trade.Symbol, confirmedExit.OrderId);
                }
                else
                {
                    _logger.LogWarning("{Symbol} is missing from broker positions, but no completed exit was found. Keeping local trade open and retrying reconciliation.", trade.Symbol);
                }
                continue;
            }

            //--------------------------------------------------
            // Quantity mismatch
            //--------------------------------------------------

            if (trade.Quantity != Math.Abs(brokerPosition.NetQty))
            {
                _logger.LogWarning(
                    "{Symbol} quantity mismatch Local={Local} Broker={Broker}",
                    trade.Symbol,
                    trade.Quantity,
                    brokerPosition.NetQty
                );

                _tradeManager.RefreshTrade(
                    trade.Exchange,
                    trade.SymbolToken,
                    t =>
                    {
                        t.Quantity = Math.Abs(brokerPosition.NetQty);
                    }
                );

                var updatedTrade = _tradeManager.GetByPosition(trade.Exchange, trade.SymbolToken);
                if (updatedTrade != null)
                {
                    await _tradeManager.UpdateAsync(updatedTrade);
                }
            }

            //--------------------------------------------------
            // Entry price mismatch
            //--------------------------------------------------

            var brokerEntryPrice = trade.Direction == TradeDirection.Short ? brokerPosition.SellAvgPrice : brokerPosition.BuyAvgPrice;

            if (trade.EntryPrice != brokerEntryPrice)
            {
                _logger.LogWarning(
                    "{Symbol} entry mismatch Local={Local} Broker={Broker}",
                    trade.Symbol,
                    trade.EntryPrice,
                    brokerPosition.BuyAvgPrice
                );

                _tradeManager.RefreshTrade(
                    trade.Exchange,
                    trade.SymbolToken,
                    t =>
                    {
                        decimal brokerEntry = brokerEntryPrice;

                        t.EntryPrice = brokerEntry;

                        //--------------------------------------------------
                        // Preserve analytics
                        //--------------------------------------------------

                        if (t.HighestPrice <= 0)
                            t.HighestPrice = brokerEntry;

                        if (t.LowestPrice <= 0)
                            t.LowestPrice = brokerEntry;

                        if (t.InitialStopLoss <= 0 && t.StopLoss > 0)
                            t.InitialStopLoss = t.StopLoss;


                        if (t.InitialRiskPerShare <= 0 && t.InitialStopLoss > 0)
                        {
                            t.InitialRiskPerShare = t.Direction == TradeDirection.Short
                                ? Math.Max(0m, t.InitialStopLoss - brokerEntry)
                                : Math.Max(0m, brokerEntry - t.InitialStopLoss);
                        }
                    }
                );

                var updatedTrade = _tradeManager.GetByPosition(trade.Exchange, trade.SymbolToken);
                if (updatedTrade != null)
                {
                    await _tradeManager.UpdateAsync(updatedTrade);
                }
            }

            //--------------------------------------------------
            // Refresh WatchList
            //--------------------------------------------------

            if (!string.IsNullOrWhiteSpace(trade.SymbolToken))
            {
                _watchList.MarkOwned(
                    trade.SymbolToken,
                    trade.Quantity,
                    trade.EntryPrice,
                    trade.StopLoss,
                    trade.Direction
                );
            }

            // Remove from recovery list
            brokerPositions.Remove(tradePositionKey);
        }

        //------------------------------------------------------
        // Recover unmanaged broker positions
        //------------------------------------------------------

        foreach (var brokerPosition in brokerPositions.Values)
        {
            if (_tradeManager.ContainsPosition(brokerPosition.Exchange, brokerPosition.SymbolToken))
                continue;

            _logger.LogWarning(
                """
                =========================================

                RECOVERING BROKER POSITION

                Symbol : {Symbol}
                Qty    : {Qty}
                Price  : {Price}

                =========================================
                """,
                brokerPosition.TradingSymbol,
                brokerPosition.NetQty,
                brokerPosition.BuyAvgPrice
            );

            var watch = _watchList.Get(brokerPosition.SymbolToken);
            var instrument = await _instrumentService.LoadInstruments();
            //if (instrument == null || instrument.InstrumentType != InstrumentType.Equity || brokerPosition.NetQty <= 0)
            //{
            //    _logger.LogWarning("Ignoring unsupported or short broker position {Symbol}. Only long equity positions are reconciled.", brokerPosition.TradingSymbol);
            //    continue;
            //}

            var trade = new TradeOrder
            {
                Symbol = brokerPosition.TradingSymbol.Trim().ToUpperInvariant(),

                SymbolToken = brokerPosition.SymbolToken,
                //Exchange = string.IsNullOrWhiteSpace(brokerPosition.Exchange) ? (instrument?.exch_seg ?? "NSE") : brokerPosition.Exchange,
                ProductType = string.IsNullOrWhiteSpace(brokerPosition.ProductType) ? "INTRADAY" : brokerPosition.ProductType,
                InstrumentType = InstrumentType.Equity,
                Direction = TradeDirection.Long,
                EntryTransactionType = "BUY",
                Quantity = Math.Abs(brokerPosition.NetQty),
                EntryPrice = brokerPosition.BuyAvgPrice,
                HighestPrice = brokerPosition.BuyAvgPrice,
                LowestPrice = brokerPosition.BuyAvgPrice,
                EntryTime = DateTime.UtcNow,
                StopLoss = watch?.StopLoss ?? 0m,

                InitialStopLoss = watch?.StopLoss ?? 0m,

                Status = TradeStatus.Open,
                Closed = false,
                TrailingActivated = false,
            };

            IndicatorMapper.ApplyTradeIndicators(trade, watch);

            await _tradeManager.SaveAsync(trade);

            _symbolLocks.Restore(trade.Symbol);

            if (!string.IsNullOrWhiteSpace(trade.SymbolToken))
            {
                _watchList.MarkOwned(
                    trade.SymbolToken,
                    trade.Quantity,
                    trade.EntryPrice,
                    trade.StopLoss,
                    trade.Direction
                );
            }

            _logger.LogInformation(
                "{Symbol} successfully recovered into TradeManager.",
                trade.Symbol
            );
        }

        await SynchronizeRecoveryFilesAsync();

        _logger.LogInformation(
            "Broker reconciliation completed. Active Trades={Count}",
            _tradeManager.GetOpen().Count
        );
    }

    private async Task SynchronizeRecoveryFilesAsync()
    {
        var persistedTrades = await _repository.GetOpenTradesAsync();

        var existingPositions = persistedTrades
            .Select(PositionIdentity.Create)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var trade in _tradeManager.GetOpen())
        {
            if (existingPositions.Contains(PositionIdentity.Create(trade)))
                continue;

            try
            {
                await _repository.SaveAsync(trade);

                _logger.LogInformation("Recovered trade {Symbol} into recovery file.", trade.Symbol);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to synchronize trade {Symbol} to recovery file.",
                    trade.Symbol
                );
            }
        }
    }
}
