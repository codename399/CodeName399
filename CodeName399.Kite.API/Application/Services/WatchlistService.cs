using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.SignalRHubs;
using CodeName399.Kite.API.Prediction;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace CodeName399.Kite.API.Services;

public class WatchListService
{
    private readonly ConcurrentDictionary<string, Gainer> _watchList = new();
    private readonly ILogger<WatchListService> _logger;
    private readonly TradingSettingsProvider _settings;
    private readonly IHubContext<MarketHub> _marketHub;
    private readonly ExpectedPredictionService _prediction;

    public WatchListService(
        ILogger<WatchListService> logger,
        TradingSettingsProvider settings,
        IHubContext<MarketHub> marketHub,
        ExpectedPredictionService prediction)
    {
        _logger = logger;
        _settings = settings;
        _marketHub = marketHub;
        _prediction = prediction;
    }

    public IReadOnlyCollection<Gainer> Current => _watchList.Values.ToList();

    /// <summary>
    /// Replaces discovery candidates for the active instrument type while
    /// preserving owned positions from other instrument profiles.
    /// </summary>
    private static void SuppressEquitySell(Gainer stock)
    {
        // Equity stocks are long-only in this system. A SELL signal is an exit
        // action, not an entry signal, and must not be exposed as a scanner signal.
        if (stock.InstrumentType == InstrumentType.Equity
            && (string.Equals(stock.Signal, "SELL", StringComparison.OrdinalIgnoreCase)
                || stock.Direction == TradeDirection.Short))
        {
            // Equity is long-only. Normalize both fields because EntryTransactionType
            // is derived from Direction and would otherwise still expose SELL even
            // when Signal has been changed to HOLD.
            stock.Signal = "HOLD";
            stock.Direction = TradeDirection.Long;
            if (string.IsNullOrWhiteSpace(stock.Reason))
                stock.Reason = "Equity short signal suppressed";
        }
    }

    public void UpdateForInstrumentType(IEnumerable<Gainer> gainers, InstrumentType instrumentType)
    {
        var incoming = gainers
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SymbolToken))
            .Where(x => x.InstrumentType == instrumentType || x.IsOwned)
            .Select(x =>
            {
                SuppressEquitySell(x);
                return x;
            })
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _watchList)
        {
            var stock = pair.Value;
            // Keep open/owned positions so they can still be managed safely.
            if (stock.IsOwned)
                continue;

            // Remove every non-owned candidate belonging to another instrument.
            if (stock.InstrumentType != instrumentType || !incoming.ContainsKey(pair.Key))
                _watchList.TryRemove(pair.Key, out _);
        }

        foreach (var gainer in incoming.Values)
            _watchList[gainer.SymbolToken] = gainer;

        _logger.LogDebug("FLOW STAGE Watchlist.UpdateForInstrumentType InstrumentType={InstrumentType}, Incoming={Incoming}, Stored={Stored}, NonOwnedActive={NonOwnedActive}, Owned={Owned}",
            instrumentType, incoming.Count, _watchList.Count,
            _watchList.Values.Count(x => !x.IsOwned && x.InstrumentType == instrumentType),
            _watchList.Values.Count(x => x.IsOwned));
    }

    public int ClearNonOwnedForInstrumentType(InstrumentType activeInstrumentType)
    {
        var removed = 0;

        foreach (var pair in _watchList)
        {
            if (pair.Value.IsOwned)
                continue;

            if (_watchList.TryRemove(pair.Key, out _))
                removed++;
        }

        _logger.LogDebug("FLOW STAGE Watchlist.ClearNonOwnedForInstrumentType InstrumentType={InstrumentType}, Removed={Removed}, OwnedPreserved={OwnedPreserved}",
            activeInstrumentType, removed, _watchList.Values.Count(x => x.IsOwned));

        return removed;
    }

    public void Update(IEnumerable<Gainer> gainers)
    {
        var latest = gainers.GroupBy(x => x.SymbolToken).ToDictionary(x => x.Key, x => x.First());

        // Remove symbols no longer in watchlist.
        foreach (var symbol in _watchList.Keys)
        {
            if (!latest.ContainsKey(symbol))
            {
                _watchList.TryRemove(symbol, out _);
            }
        }

        // Add/update symbols.
        foreach (var gainer in gainers)
        {
            SuppressEquitySell(gainer);
            _watchList[gainer.SymbolToken] = gainer;
        }

        _logger.LogDebug("FLOW STAGE Watchlist.Update Incoming={Incoming}, Stored={Stored}, Owned={Owned}, Buy={Buy}, Sell={Sell}, Loading={Loading}",
            latest.Count, _watchList.Count, _watchList.Values.Count(x => x.IsOwned),
            _watchList.Values.Count(x => string.Equals(x.Signal, "BUY", StringComparison.OrdinalIgnoreCase)),
            _watchList.Values.Count(x => string.Equals(x.Signal, "SELL", StringComparison.OrdinalIgnoreCase)),
            _watchList.Values.Count(x => string.Equals(x.Signal, "LOADING", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// Removes normal discovery candidates when the market is closed.
    ///
    /// Open/owned positions and explicitly protected symbols
    /// (for example active active trades) are preserved.
    /// </summary>
    public int RemoveNonActive(ISet<string>? protectedSymbolTokens = null)
    {
        protectedSymbolTokens ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var removed = 0;

        foreach (var pair in _watchList)
        {
            var stock = pair.Value;

            // ---------------------------------------------------------
            // Never remove an owned/open position.
            // ---------------------------------------------------------

            if (stock.IsOwned)
                continue;

            // ---------------------------------------------------------
            // Never remove an explicitly protected symbol.
            // This is used for active active trades.
            // ---------------------------------------------------------

            if (protectedSymbolTokens.Contains(stock.SymbolToken))
            {
                continue;
            }

            if (_watchList.TryRemove(pair.Key, out _))
            {
                removed++;
            }
        }

        return removed;
    }

    public bool TryGet(string symbolToken, out Gainer gainer)
    {
        return _watchList.TryGetValue(symbolToken, out gainer!);
    }

    public void UpdateTick(MarketTick tick)
    {
        _prediction.ObserveTick(tick);
        if (!_watchList.TryGetValue(tick.SymbolToken, out var stock))
        {
            return;
        }

        // Copy the complete broker tick, not only LTP/bid/ask. This keeps every
        // subscribed-stock tick field available to the UI and to downstream services.
        stock.CurrentPrice = tick.Ltp;
        stock.LastTickUpdated = tick.LastUpdated;
        stock.TickExchangeType = tick.ExchangeType;
        stock.TickSequenceNumber = tick.SequenceNumber;
        stock.TickExchangeTime = tick.ExchangeTime;
        stock.TickLtp = tick.Ltp;
        stock.TickBid = tick.Bid;
        stock.TickAsk = tick.Ask;
        stock.TickOpen = tick.Open;
        stock.TickHigh = tick.High;
        stock.TickLow = tick.Low;
        stock.TickClose = tick.Close;
        stock.TickLastTradedQuantity = tick.LastTradedQuantity;
        stock.TickAveragePrice = tick.AveragePrice;
        // A websocket quote with zero DayVolume must not overwrite a valid
        // cumulative volume already obtained from the quote/REST feed.
        if (tick.DayVolume > 0)
            stock.TickDayVolume = tick.DayVolume;
        stock.TickTotalBuyQuantity = tick.TotalBuyQuantity;
        stock.TickTotalSellQuantity = tick.TotalSellQuantity;
        stock.TickPreviousClose = tick.PreviousClose;
        stock.TickVolume = tick.Volume;

        if (stock.Quote != null)
        {
            stock.Quote.Ltp = tick.Ltp;
            stock.Quote.Bid = tick.Bid;
            stock.Quote.Ask = tick.Ask;
            stock.Quote.High = tick.High;
            stock.Quote.Low = tick.Low;
            stock.Quote.Close = tick.Close;
            stock.Quote.Open = tick.Open;
            stock.Quote.LastTradedQuantity = tick.LastTradedQuantity;
            stock.Quote.AveragePrice = tick.AveragePrice;
            if (tick.DayVolume > 0)
            {
                stock.Quote.TradeVolume = tick.DayVolume;
                stock.Quote.DayVolume = tick.DayVolume;
            }
            stock.Quote.TotalBuyQuantity = tick.TotalBuyQuantity;
            stock.Quote.TotalSellQuantity = tick.TotalSellQuantity;
            stock.Quote.PreviousClose = tick.PreviousClose;
            stock.Quote.Volume = tick.Volume;
        }
        stock.Bid = tick.Bid;
        stock.Ask = tick.Ask;
        stock.Spread = tick.Spread;
        stock.SpreadPercent = tick.SpreadPercent;

        if (stock.PreviousClose > 0)
        {
            stock.ChangePercent = Math.Round(
                ((tick.Ltp - stock.PreviousClose) / stock.PreviousClose) * 100m,
                2
            );
        }

        UpdatePriority(stock);

        // ---------------------------------------------------------
        // Refresh P&L on every tick.
        // ---------------------------------------------------------

        if (stock.IsOwned && stock.Quantity > 0 && stock.AveragePrice > 0)
        {
            stock.InvestedAmount = stock.AveragePrice * stock.Quantity;

            stock.CurrentValue = stock.CurrentPrice * stock.Quantity;

            stock.ProfitLoss = stock.Direction == TradeDirection.Short
                ? stock.InvestedAmount - stock.CurrentValue
                : stock.CurrentValue - stock.InvestedAmount;

            stock.ProfitLossPercentage =
                stock.InvestedAmount == 0
                    ? 0
                    : Math.Round(stock.ProfitLoss / stock.InvestedAmount * 100m, 2);
        }

        // Push the entire subscribed-stock snapshot on every broker tick. The UI
        // merges this object into its existing row, so no full watchlist refresh is
        // required and every tick field remains available.
        _ = _marketHub.Clients.All.SendAsync("StockTickUpdated", stock);
    }

    public void UpdateIndicators(string symbolToken, IndicatorSnapshot indicator)
    {
        if (!_watchList.TryGetValue(symbolToken, out var stock))
        {
            return;
        }

        IndicatorMapper.ApplyIndicators(stock, indicator);

        UpdatePriority(stock);
    }

    public void MarkOwned(
        string symbolToken,
        int quantity,
        decimal entryPrice,
        decimal stopLoss,
        TradeDirection direction = TradeDirection.Long
    )
    {
        if (!_watchList.TryGetValue(symbolToken, out var stock))
        {
            return;
        }

        stock.IsOwned = true;
        stock.Direction = direction;

        stock.Signal = "HOLD";

        stock.Reason = "Position Open";

        stock.Quantity = quantity;

        stock.AveragePrice = entryPrice;

        stock.StopLoss = stopLoss;


        stock.InvestedAmount = entryPrice * quantity;

        stock.CurrentValue = stock.CurrentPrice * quantity;

        stock.ProfitLoss = direction == TradeDirection.Short
            ? stock.InvestedAmount - stock.CurrentValue
            : stock.CurrentValue - stock.InvestedAmount;

        stock.ProfitLossPercentage =
            stock.InvestedAmount == 0
                ? 0
                : Math.Round(stock.ProfitLoss / stock.InvestedAmount * 100m, 2);
    }

    public void MarkSold(string symbolToken)
    {
        if (!_watchList.TryGetValue(symbolToken, out var stock))
        {
            return;
        }

        stock.IsOwned = false;

        stock.Quantity = 0;

        stock.AveragePrice = 0;

        stock.StopLoss = 0;


        stock.ProfitLoss = 0;

        stock.ProfitLossPercentage = 0;

        stock.InvestedAmount = 0;

        stock.CurrentValue = 0;

        stock.Signal = "HOLD";

        stock.Reason = "No Position";
    }

    public Gainer? Get(string symbolToken)
    {
        if (_watchList.TryGetValue(symbolToken, out var stock))
        {
            return stock;
        }

        return null;
    }

    private void UpdatePriority(Gainer stock)
    {
        // Priority is display ordering only; it never gates entry.
        var priority = (int)Math.Round(Math.Max(0m, stock.ChangePercent) * 10m);
        if (stock.LastHistoricalUpdate != default)
            priority += Math.Min((int)(DateTime.UtcNow - stock.LastHistoricalUpdate).TotalMinutes, 60);
        if (stock.IsSubscribed) priority = 0;

        stock.Priority = Math.Max(priority, 1);
    }

    public IReadOnlyList<Gainer> GetEntryCandidates()
    {
        // Do not pre-filter by scanner output. Entry eligibility is decided exclusively by
        // risk/charges validation before execution.
        return _watchList.Values
            .Where(x => x.InstrumentType == InstrumentType.Equity)
            .Where(x => x.IsSubscribed && !x.IsOwned)
            .ToList();
    }

    public IReadOnlyList<Gainer> GetBuyCandidates() => GetEntryCandidates();

    public bool IsSubscribed(string symbolToken)
    {
        if (string.IsNullOrWhiteSpace(symbolToken))
        {
            return false;
        }

        if (!TryGet(symbolToken, out var stock))
        {
            return false;
        }

        return stock.IsSubscribed;
    }
}
