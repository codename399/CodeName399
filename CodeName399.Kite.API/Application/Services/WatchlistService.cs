using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.Services.Interface;
using CodeName399.Kite.API.SignalRHubs;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace CodeName399.Kite.API.Services;

public class WatchListService
{
    private readonly ConcurrentDictionary<string, Gainer> _watchList = new();
    private readonly ConcurrentDictionary<string, byte> _netProfitOpportunityNotified = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _pullbackOpportunityNotifiedAt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _pullbackNotificationLastAttempt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _pullbackNotificationInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, LiveCandleState> _liveCandles = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _ownedChargeEstimateLastAttempt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _ownedChargeEstimateInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTime> _minimumChargeEstimateLastAttempt = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, byte> _minimumChargeEstimateInFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _balanceRefreshLock = new(1, 1);
    private decimal _availableBalanceCache;
    private DateTime _availableBalanceCacheAt = DateTime.MinValue;
    private readonly IAngelOneService _angel;
    private readonly ILogger<WatchListService> _logger;
    private readonly TradingSettingsProvider _settings;
    private readonly IHubContext<MarketHub> _marketHub;

    public WatchListService(
        ILogger<WatchListService> logger,
        TradingSettingsProvider settings,
        IHubContext<MarketHub> marketHub,
        IAngelOneService angel)
    {
        _logger = logger;
        _settings = settings;
        _marketHub = marketHub;
        _angel = angel;
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
                if (_watchList.TryGetValue(x.SymbolToken, out var existing))
                {
                    x.IsManuallySubscribed = existing.IsManuallySubscribed;
                    x.IsManuallyUnsubscribed = existing.IsManuallyUnsubscribed;
                    x.IsSubscribed = existing.IsManuallyUnsubscribed ? false : true;
                    // Preserve the latest live-candle classification until a new candle closes.
                    x.PullbackDetected = existing.PullbackDetected;
                    x.OwnedEstimatedCharges = existing.OwnedEstimatedCharges > 0m ? existing.OwnedEstimatedCharges : x.OwnedEstimatedCharges;
                }
                return x;
            })
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _watchList)
        {
            var stock = pair.Value;
            // Keep open/owned positions so they can still be managed safely.
            if (stock.IsOwned || stock.IsManuallySubscribed || stock.IsManuallyUnsubscribed)
                continue;

            // Remove every normal discovery candidate belonging to another instrument.
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
            if (pair.Value.IsOwned || pair.Value.IsManuallySubscribed || pair.Value.IsManuallyUnsubscribed)
                continue;

            if (_watchList.TryRemove(pair.Key, out _))
                removed++;
        }

        _logger.LogDebug("FLOW STAGE Watchlist.ClearNonOwnedForInstrumentType InstrumentType={InstrumentType}, Removed={Removed}, OwnedPreserved={OwnedPreserved}",
            activeInstrumentType, removed, _watchList.Values.Count(x => x.IsOwned));

        return removed;
    }

    public bool TryGet(string symbolToken, out Gainer gainer)
    {
        return _watchList.TryGetValue(symbolToken, out gainer!);
    }

    public void AddManualSubscription(Gainer gainer)
    {
        if (string.IsNullOrWhiteSpace(gainer.SymbolToken))
            throw new ArgumentException("A symbol token is required for a manual subscription.", nameof(gainer));
        gainer.IsSubscribed = true;
        gainer.IsManuallySubscribed = true;
        gainer.IsManuallyUnsubscribed = false;
        _watchList[gainer.SymbolToken] = gainer;
        PublishSnapshot();
    }

    public bool SetSubscription(string symbolToken, bool subscribed, bool manual = true)
    {
        if (!_watchList.TryGetValue(symbolToken, out var stock))
            return false;

        stock.IsSubscribed = subscribed;
        if (manual)
        {
            stock.IsManuallySubscribed = subscribed;
            stock.IsManuallyUnsubscribed = !subscribed;
        }
        PublishSnapshot();
        return true;
    }

    public void RemoveManualSubscription(string symbolToken)
    {
        // Keep the candidate visible so the user can resubscribe it from the dashboard.
        SetSubscription(symbolToken, false, manual: true);
    }

    public void PublishSnapshot()
    {
        var snapshot = Current
            .OrderByDescending(x => x.IsOwned)
            .ThenByDescending(x => x.PullbackDetected)
            .ThenByDescending(x => x.IsManuallySubscribed)
            .ThenByDescending(x => x.ChangePercent)
            .ToList();
        _ = _marketHub.Clients.All.SendAsync("GainersUpdated", snapshot);
    }

    public void UpdateTick(MarketTick tick)
    {
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

        // Recalculate the minimum quantity against the current tick price.
        // EstimatedCharges is refreshed by the paged dashboard endpoint; the
        // price-dependent quantity itself is recalculated for every tick.
        if (tick.Ltp > 0)
        {
            var expectedProfitPerShare = tick.Ltp * _settings.Current.MinimumBuyProfitChangePercent / 100m;
            if (stock.EstimatedCharges > 0 && expectedProfitPerShare > 0)
            {
                // Re-evaluate the quantity on every tick using the most recent
                // round-trip charge estimate. The full broker estimate is refreshed
                // by the paged endpoint; this tick path only performs local math.
                stock.MinimumBuyQuantity = (int)Math.Min(int.MaxValue, Math.Max(1m, Math.Ceiling(stock.EstimatedCharges / expectedProfitPerShare)));
                var expectedProfit = stock.MinimumBuyQuantity * expectedProfitPerShare;
                if (expectedProfit <= stock.EstimatedCharges)
                    stock.MinimumBuyQuantity++;
                stock.MinimumInvestmentWithCharges = stock.MinimumBuyQuantity * tick.Ltp + stock.EstimatedCharges;
                stock.SufficientFunds = null; // UI rechecks against the latest account balance.
            }
            else
            {
                // Never advertise a one-share buy as charge-covering when no
                // broker charge estimate has been obtained yet.
                stock.MinimumBuyQuantity = 0;
                stock.MinimumInvestmentWithCharges = 0;
                stock.SufficientFunds = null;
            }
        }

        UpdatePriority(stock);
        if (stock.IsOwned)
            _ = RefreshOwnedChargeEstimateAsync(stock, tick.Ltp);
        else
            _ = RefreshMinimumBuyEstimateAsync(stock, tick.Ltp);
        UpdateLiveCandleAndPullback(stock, tick);
        TryNotifyPullbackOpportunity(stock);

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
            var netProfit = stock.ProfitLoss - Math.Max(0m, stock.OwnedEstimatedCharges);
            if (stock.OwnedEstimatedCharges > 0m && _settings.Current.EnableNotification && _settings.Current.EnableNetProfitOpportunityNotifications && netProfit > 0)
            {
                if (_netProfitOpportunityNotified.TryAdd(stock.SymbolToken, 0))
                {
                    var istNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, MarketClock.IndiaTimeZone);
                    var message = $"{stock.Symbol} has a positive estimated net-profit opportunity.\n" +
                                  $"Quantity: {stock.Quantity}\nLTP: ₹{stock.CurrentPrice:F2}\n" +
                                  $"Gross P/L: ₹{stock.ProfitLoss:F2}\nEstimated round-trip charges: ₹{stock.OwnedEstimatedCharges:F2}\n" +
                                  $"Estimated net P/L: ₹{netProfit:F2}\nTime (IST): {istNow:yyyy-MM-dd HH:mm:ss}";
                    _ = Task.Run(() => EmailHelper.SendEmail(message, _settings.Current, $"Net-profit opportunity: {stock.Symbol}"));
                }
            }
            else if (stock.OwnedEstimatedCharges > 0m && netProfit <= 0)
            {
                _netProfitOpportunityNotified.TryRemove(stock.SymbolToken, out _);
            }
        }

        // Push the entire subscribed-stock snapshot on every broker tick. The UI
        // merges this object into its existing row, so no full watchlist refresh is
        // required and every tick field remains available.
        _ = _marketHub.Clients.All.SendAsync("StockTickUpdated", stock);
    }



    private async Task RefreshMinimumBuyEstimateAsync(Gainer stock, decimal price)
    {
        if (price <= 0m || stock.IsOwned || string.IsNullOrWhiteSpace(stock.SymbolToken)) return;
        var now = DateTime.UtcNow;
        if (_minimumChargeEstimateLastAttempt.TryGetValue(stock.SymbolToken, out var lastAttempt))
        {
            if (now - lastAttempt < TimeSpan.FromSeconds(60)) return;
        }
        else if (stock.EstimatedCharges > 0m && stock.MinimumBuyQuantity > 0)
        {
            // The dashboard endpoint already calculated this stock. Start the
            // refresh interval without duplicating those broker estimate calls.
            _minimumChargeEstimateLastAttempt.TryAdd(stock.SymbolToken, now);
            return;
        }
        if (!_minimumChargeEstimateInFlight.TryAdd(stock.SymbolToken, 0)) return;
        _minimumChargeEstimateLastAttempt[stock.SymbolToken] = now;

        try
        {
            var balance = await GetCachedAvailableBalanceAsync();
            var expectedProfitPerShare = price * _settings.Current.MinimumBuyProfitChangePercent / 100m;
            var maximumAffordable = (int)Math.Min(int.MaxValue, Math.Max(0m, Math.Floor(balance / price)));
            if (expectedProfitPerShare <= 0m || maximumAffordable < 1)
            {
                stock.MinimumBuyQuantity = 0;
                stock.EstimatedCharges = 0m;
                stock.MinimumInvestmentWithCharges = 0m;
                stock.SufficientFunds = false;
                return;
            }

            var oneShareCharges = await EstimateTransactionChargesAsync(stock, 1, price, "BUY")
                + await EstimateTransactionChargesAsync(stock, 1, price, "SELL");
            if (oneShareCharges <= 0m) return;

            var quantity = Math.Min(maximumAffordable, (int)Math.Min(int.MaxValue, Math.Max(1m, Math.Ceiling(oneShareCharges / expectedProfitPerShare))));
            var charges = oneShareCharges;
            var coversCharges = false;
            // The one-share estimate gives a close starting point. A few exact
            // broker estimates account for quantity-sensitive fees without
            // creating an unbounded request loop from a live tick callback.
            for (var attempt = 0; attempt < 8 && quantity <= maximumAffordable; attempt++)
            {
                var buyCharges = await EstimateTransactionChargesAsync(stock, quantity, price, "BUY");
                var sellCharges = await EstimateTransactionChargesAsync(stock, quantity, price, "SELL");
                if (buyCharges <= 0m || sellCharges <= 0m) return;
                charges = buyCharges + sellCharges;
                if (quantity * expectedProfitPerShare > charges)
                {
                    coversCharges = true;
                    break;
                }
                quantity++;
            }

            if (coversCharges && quantity <= maximumAffordable)
            {
                stock.MinimumBuyQuantity = quantity;
                stock.EstimatedCharges = charges;
                stock.MinimumInvestmentWithCharges = quantity * price + charges;
                stock.SufficientFunds = stock.MinimumInvestmentWithCharges <= balance;
            }
            else if (stock.MinimumBuyQuantity <= 0)
            {
                stock.MinimumBuyQuantity = 0;
                stock.EstimatedCharges = 0m;
                stock.MinimumInvestmentWithCharges = 0m;
                stock.SufficientFunds = false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to refresh minimum-buy estimate for {Symbol}.", stock.Symbol);
        }
        finally
        {
            _minimumChargeEstimateInFlight.TryRemove(stock.SymbolToken, out _);
        }
    }

    private async Task RefreshOwnedChargeEstimateAsync(Gainer stock, decimal price)
    {
        if (price <= 0m || stock.Quantity < 1 || string.IsNullOrWhiteSpace(stock.SymbolToken)) return;
        var now = DateTime.UtcNow;
        var quantity = stock.Quantity;
        if (_ownedChargeEstimateLastAttempt.TryGetValue(stock.SymbolToken, out var lastAttempt)
            && now - lastAttempt < TimeSpan.FromSeconds(30)) return;
        if (!_ownedChargeEstimateInFlight.TryAdd(stock.SymbolToken, 0)) return;

        _ownedChargeEstimateLastAttempt[stock.SymbolToken] = now;
        try
        {
            var buy = await EstimateTransactionChargesAsync(stock, quantity, price, "BUY");
            var sell = await EstimateTransactionChargesAsync(stock, quantity, price, "SELL");
            if (stock.IsOwned && stock.Quantity == quantity && buy > 0m && sell > 0m)
                stock.OwnedEstimatedCharges = buy + sell;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Unable to refresh charge estimate for owned stock {Symbol}.", stock.Symbol);
        }
        finally
        {
            _ownedChargeEstimateInFlight.TryRemove(stock.SymbolToken, out _);
        }
    }

    private async Task<decimal> GetCachedAvailableBalanceAsync()
    {
        await _balanceRefreshLock.WaitAsync();
        try
        {
            if (DateTime.UtcNow - _availableBalanceCacheAt < TimeSpan.FromSeconds(5))
                return _availableBalanceCache;
            _availableBalanceCache = await _angel.GetAvailableBalanceAsync();
            _availableBalanceCacheAt = DateTime.UtcNow;
            return _availableBalanceCache;
        }
        finally
        {
            _balanceRefreshLock.Release();
        }
    }

    private async Task<decimal> EstimateTransactionChargesAsync(Gainer stock, int quantity, decimal price, string transactionType)
    {
        var response = await _angel.EstimateChargesAsync(new BrokerageEstimateRequest
        {
            Orders = [new BrokerageEstimateOrder
            {
                ProductType = stock.ProductType,
                TransactionType = transactionType,
                Quantity = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Price = price.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Exchange = stock.Exchange,
                SymbolName = stock.Symbol,
                Token = stock.SymbolToken
            }]
        });
        if (!response.Status) return 0m;
        return response.Data?.Summary?.Breakup?.Sum(x => x.Amount) ?? 0m;
    }

    private void UpdateLiveCandleAndPullback(Gainer stock, MarketTick tick)
    {
        if (tick.Ltp <= 0 || string.IsNullOrWhiteSpace(stock.SymbolToken)) return;

        var timestamp = tick.LastUpdated == default ? DateTime.UtcNow : tick.LastUpdated;
        if (timestamp.Kind == DateTimeKind.Unspecified)
            timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        if (timestamp.Kind == DateTimeKind.Local)
            timestamp = timestamp.ToUniversalTime();
        var india = TimeZoneInfo.ConvertTimeFromUtc(timestamp, MarketClock.IndiaTimeZone);
        var minute = new DateTime(india.Year, india.Month, india.Day, india.Hour, india.Minute, 0, DateTimeKind.Unspecified);
        var state = _liveCandles.GetOrAdd(stock.SymbolToken, _ => new LiveCandleState(minute, tick.Ltp, tick.DayVolume));
        var pullbackDetected = false;

        lock (state.Sync)
        {
            if (minute < state.Current.Timestamp) return;
            if (minute == state.Current.Timestamp)
            {
                state.Current.High = Math.Max(state.Current.High, tick.Ltp);
                state.Current.Low = Math.Min(state.Current.Low, tick.Ltp);
                state.Current.Close = tick.Ltp;
                state.Current.Volume = Math.Max(state.Current.Volume, tick.DayVolume);
                return;
            }

            var closedCandle = state.Current;
            // Definition used for the dashboard flag: two rising closes followed by
            // a red candle that remains above the earlier candle's low, while the
            // stock is still positive versus previous close. This is a display/
            // notification heuristic, not an order trigger.
            pullbackDetected = state.OlderClosed is not null
                && state.PreviousClosed is not null
                && state.OlderClosed.Close > 0
                && state.PreviousClosed.Close > state.OlderClosed.Close
                && closedCandle.Close < closedCandle.Open
                && closedCandle.Close < state.PreviousClosed.Close
                && closedCandle.Close > state.OlderClosed.Low
                && stock.ChangePercent > 0
                && !stock.IsOwned;

            state.OlderClosed = state.PreviousClosed;
            state.PreviousClosed = closedCandle;
            state.Current = new Candle
            {
                Timestamp = minute,
                Open = tick.Ltp,
                High = tick.Ltp,
                Low = tick.Ltp,
                Close = tick.Ltp,
                Volume = tick.DayVolume
            };
            stock.PullbackDetected = pullbackDetected;
        }

    }

    private void TryNotifyPullbackOpportunity(Gainer stock)
    {
        if (!stock.PullbackDetected
            || stock.IsOwned
            || stock.MinimumBuyQuantity < 1
            || stock.EstimatedCharges <= 0m
            || stock.MinimumInvestmentWithCharges <= 0m
            || !_settings.Current.EnableNotification
            || !_settings.Current.EnablePullbackOpportunityNotifications
            || !_liveCandles.TryGetValue(stock.SymbolToken, out var state)) return;

        DateTime candleTimestamp;
        Candle? candle;
        lock (state.Sync)
        {
            candle = state.PreviousClosed;
            candleTimestamp = candle?.Timestamp ?? DateTime.MinValue;
        }
        if (candle is null || candleTimestamp == DateTime.MinValue) return;
        if (_pullbackOpportunityNotifiedAt.TryGetValue(stock.SymbolToken, out var notifiedAt)
            && notifiedAt == candleTimestamp) return;
        var now = DateTime.UtcNow;
        if (_pullbackNotificationLastAttempt.TryGetValue(stock.SymbolToken, out var lastAttempt)
            && now - lastAttempt < TimeSpan.FromSeconds(10)) return;
        if (!_pullbackNotificationInFlight.TryAdd(stock.SymbolToken, 0)) return;
        _pullbackNotificationLastAttempt[stock.SymbolToken] = now;

        _ = SendPullbackOpportunityNotificationAsync(stock, candle, candleTimestamp);
    }

    private async Task SendPullbackOpportunityNotificationAsync(Gainer stock, Candle candle, DateTime candleTimestamp)
    {
        try
        {
            var balance = await GetCachedAvailableBalanceAsync();
            if (stock.IsOwned || stock.MinimumInvestmentWithCharges <= 0m || stock.MinimumInvestmentWithCharges > balance)
                return;
            if (_pullbackOpportunityNotifiedAt.TryGetValue(stock.SymbolToken, out var notifiedAt)
                && notifiedAt == candleTimestamp) return;

            var message = $"{stock.Symbol} closed a pullback candle while remaining positive versus previous close.\n"
                + $"Candle close: ₹{candle.Close:F2}\nChange: {stock.ChangePercent:F2}%\n"
                + $"Minimum quantity: {stock.MinimumBuyQuantity}\nEstimated round-trip charges: ₹{stock.EstimatedCharges:F2}\n"
                + $"Minimum investment including estimated charges: ₹{stock.MinimumInvestmentWithCharges:F2}\n"
                + $"Available funds: ₹{balance:F2}\nCandle time (IST): {candle.Timestamp:yyyy-MM-dd HH:mm}\n"
                + "Review the chart before placing a manual buy order.";
            var sent = await Task.Run(() => EmailHelper.SendEmail(message, _settings.Current, $"Pullback opportunity: {stock.Symbol}"));
            if (sent) _pullbackOpportunityNotifiedAt[stock.SymbolToken] = candleTimestamp;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to send pullback opportunity notification for {Symbol}.", stock.Symbol);
        }
        finally
        {
            _pullbackNotificationInFlight.TryRemove(stock.SymbolToken, out _);
        }
    }

    private sealed class LiveCandleState
    {
        public object Sync { get; } = new();
        public Candle Current { get; set; }
        public Candle? PreviousClosed { get; set; }
        public Candle? OlderClosed { get; set; }

        public LiveCandleState(DateTime timestamp, decimal price, decimal volume)
        {
            Current = new Candle { Timestamp = timestamp, Open = price, High = price, Low = price, Close = price, Volume = volume };
        }
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
        _availableBalanceCacheAt = DateTime.MinValue;
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
        PublishSnapshot();
    }

    public void MarkSold(string symbolToken)
    {
        if (!_watchList.TryGetValue(symbolToken, out var stock))
        {
            return;
        }

        stock.IsOwned = false;
        _availableBalanceCacheAt = DateTime.MinValue;

        stock.Quantity = 0;

        stock.AveragePrice = 0;

        stock.StopLoss = 0;


        stock.ProfitLoss = 0;

        stock.ProfitLossPercentage = 0;

        stock.InvestedAmount = 0;

        stock.CurrentValue = 0;

        stock.Signal = "HOLD";

        stock.Reason = "No Position";
        stock.OwnedEstimatedCharges = 0m;
        _netProfitOpportunityNotified.TryRemove(symbolToken, out _);
        _pullbackOpportunityNotifiedAt.TryRemove(symbolToken, out _);
        _pullbackNotificationLastAttempt.TryRemove(symbolToken, out _);
        _ownedChargeEstimateLastAttempt.TryRemove(symbolToken, out _);
        _minimumChargeEstimateLastAttempt.TryRemove(symbolToken, out _);
        PublishSnapshot();
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
