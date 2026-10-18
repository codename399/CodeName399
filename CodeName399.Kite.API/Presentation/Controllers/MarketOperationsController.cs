using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Configuration;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using CodeName399.Kite.API.SignalRHubs;

namespace CodeName399.Kite.API.Controllers;

[Authorize]
[ApiController]
[Route("api/market")]
public sealed class MarketOperationsController : ControllerBase
{
    private readonly WatchListService _watchList;
    private readonly IAngelMarketService _market;
    private readonly IAngelOneService _angel;
    private readonly IInstrumentService _instruments;
    private readonly IHistoricalDataService _history;
    private readonly IMarketDataProvider _marketData;
    private readonly TradingSettingsProvider _settings;
    private readonly MarketClock _clock;
    private readonly TradeManager _trades;
    private readonly PaperPortfolioService _paperPortfolio;
    private readonly ILogger<MarketOperationsController> _logger;

    public MarketOperationsController(
        WatchListService watchList,
        IAngelMarketService market,
        IAngelOneService angel,
        IInstrumentService instruments,
        IHistoricalDataService history,
        IMarketDataProvider marketData,
        TradingSettingsProvider settings,
        MarketClock clock,
        TradeManager trades,
        PaperPortfolioService paperPortfolio,
        ILogger<MarketOperationsController> logger)
    {
        _watchList = watchList;
        _market = market;
        _angel = angel;
        _instruments = instruments;
        _history = history;
        _marketData = marketData;
        _settings = settings;
        _clock = clock;
        _trades = trades;
        _paperPortfolio = paperPortfolio;
        _logger = logger;
    }

    [HttpGet("gainers")]
    public async Task<IActionResult> GetGainers([FromQuery] int page = 1, [FromQuery] int? pageSize = null, [FromQuery] string? search = null, CancellationToken cancellationToken = default)
    {
        if (_watchList.Current.Count == 0)
        {
            var gainers = await _market.GetTopGainersAsync();
            _watchList.UpdateForInstrumentType(gainers, InstrumentType.Equity);
            try
            {
                await _marketData.SetSubscriptionsAsync(_watchList.Current
                    .Where(x => x.InstrumentType == InstrumentType.Equity && !x.IsManuallyUnsubscribed)
                    .Select(x => new MarketSubscription(x.SymbolToken, x.Exchange))
                    .ToList());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Initial market subscriptions will be retried by market discovery.");
            }
            _watchList.PublishSnapshot();
        }

        var size = Math.Clamp(pageSize ?? _settings.Current.GainersPageSize, 1, 100);
        page = Math.Max(1, page);
        var query = _watchList.Current.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x =>
                x.Symbol.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || x.SymbolToken.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                || x.Exchange.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));

        var all = query
            .OrderByDescending(x => x.IsOwned)
            .ThenByDescending(x => x.PullbackDetected)
            .ThenByDescending(x => x.IsManuallySubscribed)
            .ThenByDescending(x => x.ChangePercent)
            .ToList();

        var items = all.Skip((page - 1) * size).Take(size).ToList();
        foreach (var item in items)
            await PopulateExecutionEstimatesAsync(item);
        return Ok(new { page, pageSize = size, totalCount = all.Count, totalPages = (int)Math.Ceiling(all.Count / (double)size), items });
    }

    [HttpGet("search")]
    public Task<IActionResult> Search([FromQuery] string query, [FromQuery] int page = 1, [FromQuery] int? pageSize = null)
        => GetGainers(page, pageSize, query);

    [HttpGet("{symbolToken}/candles")]
    public async Task<ActionResult<IReadOnlyList<MarketCandleResponse>>> GetTodayCandles([FromRoute] string symbolToken, [FromQuery] string exchange = "NSE", [FromQuery] string interval = "ONE_MINUTE")
    {
        if (string.IsNullOrWhiteSpace(symbolToken)) return BadRequest("symbolToken is required.");
        var login = await _angel.GetLoginDataAsync();
        var candles = await _history.GetAsync(symbolToken, login.JwtToken, interval, exchange);
        var today = DateOnly.FromDateTime(_clock.Now);
        var result = candles
            .Where(c => DateOnly.FromDateTime(ToIndiaTime(c.Timestamp)) == today)
            .OrderBy(c => c.Timestamp)
            .Select(c => new MarketCandleResponse(
                new DateTimeOffset(DateTime.SpecifyKind(ToIndiaTime(c.Timestamp), DateTimeKind.Unspecified), TimeSpan.FromHours(5.5)),
                c.Open, c.High, c.Low, c.Close, c.Volume))
            .ToList();
        return Ok(result);
    }

    [HttpPost("{symbolToken}/subscribe")]
    public async Task<IActionResult> Subscribe([FromRoute] string symbolToken, [FromQuery] string exchange = "NSE")
    {
        var instrument = await _instruments.GetByTokenAndExchangeAsync(symbolToken, exchange);
        if (instrument is null) return NotFound(new { message = "Equity instrument was not found." });
        await _marketData.SubscribeAsync([symbolToken], exchange);
        var quote = _marketData.Get(symbolToken);
        if (_watchList.TryGet(symbolToken, out _))
        {
            _watchList.SetSubscription(symbolToken, subscribed: true);
        }
        else
        {
            _watchList.AddManualSubscription(new Gainer
            {
                Symbol = instrument.symbol,
                SymbolToken = instrument.token,
                Exchange = instrument.exch_seg,
                InstrumentType = InstrumentType.Equity,
                IsSubscribed = true,
                IsManuallySubscribed = true,
                CurrentPrice = quote?.Ltp ?? 0,
                LastTickUpdated = quote?.LastUpdated ?? DateTime.UtcNow,
                Quote = new MarketQuoteData { TradingSymbol = instrument.symbol, SymbolToken = instrument.token, Exchange = instrument.exch_seg, Ltp = quote?.Ltp ?? 0 }
            });
        }
        return Ok(new { success = true, symbolToken, exchange });
    }

    [HttpDelete("{symbolToken}/subscribe")]
    public async Task<IActionResult> Unsubscribe([FromRoute] string symbolToken, [FromQuery] string exchange = "NSE")
    {
        if (_trades.GetOpen().Any(x => string.Equals(x.SymbolToken, symbolToken, StringComparison.OrdinalIgnoreCase)))
            return Conflict(new { message = "An owned/open position cannot be unsubscribed." });
        await _marketData.UnsubscribeAsync([symbolToken], exchange);
        _watchList.RemoveManualSubscription(symbolToken);
        return Ok(new { success = true, symbolToken, exchange });
    }

    [HttpPost("sendemail")]
    public IActionResult SendEmail([FromBody] SendMarketEmailRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Subject) || string.IsNullOrWhiteSpace(request.Message))
            return BadRequest(new { success = false, message = "Subject and message are required." });
        var sent = EmailHelper.SendEmail(request.Message, _settings.Current, request.Subject, request.IsHtml);
        return sent ? Ok(new { success = true }) : StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Email was not sent. Check notification and SMTP configuration." });
    }

    [HttpPost("buy")]
    public async Task<IActionResult> Buy([FromBody] ManualTradeRequest request)
    {
        if (!_clock.IsMarketOpen) return BadRequest(new { success = false, message = "Buying is available only during market hours." });
        if (string.IsNullOrWhiteSpace(request.SymbolToken)) return BadRequest(new { success = false, message = "symbolToken is required." });
        var buyMode = (request.BuyMode ?? string.Empty).Trim().ToLowerInvariant();
        if (buyMode is not ("minimum" or "maximum")) return BadRequest(new { success = false, message = "BuyMode must be 'minimum' or 'maximum'." });
        if (!_watchList.TryGet(request.SymbolToken, out var stock)) return NotFound(new { success = false, message = "Stock is not in the active watchlist. Subscribe it first." });
        if (_trades.ContainsPosition(stock.Exchange, stock.SymbolToken)) return Conflict(new { success = false, message = "An open position already exists for this stock." });
        if (stock.CurrentPrice <= 0) return BadRequest(new { success = false, message = "A valid live price is not available." });

        var configuration = _settings.Current;
        var balance = configuration.PaperTrading || configuration.PaperTradingAsLive
            ? await _paperPortfolio.GetAvailableBalanceAsync()
            : await _angel.GetAvailableBalanceAsync(forceLive: true);
        var quantity = request.Quantity;
        if (buyMode == "maximum")
        {
            var budget = balance * Math.Clamp(configuration.MaxCapitalPerTradePercent, 0m, 100m) / 100m;
            quantity = (int)Math.Min(int.MaxValue, Math.Max(0m, Math.Floor(budget / stock.CurrentPrice)));
            var fitsBudget = false;
            for (var attempt = 0; attempt < 100 && quantity > 0; attempt++)
            {
                var entryCharges = await EstimateCharges(stock, quantity, stock.CurrentPrice);
                if (entryCharges <= 0m) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Broker charge estimation is unavailable; no order was placed." });
                if (stock.CurrentPrice * quantity + entryCharges <= budget) { fitsBudget = true; break; }
                quantity--;
            }
            if (!fitsBudget) quantity = 0;
        }
        else if (buyMode == "minimum")
        {
            var maximumAffordable = (int)Math.Min(int.MaxValue, Math.Max(0m, Math.Floor(balance / stock.CurrentPrice)));
            var expectedProfitPerShare = stock.CurrentPrice * configuration.MinimumBuyProfitChangePercent / 100m;
            var oneShareRoundTripCharges = await EstimateRoundTripCharges(stock, 1, stock.CurrentPrice);
            if (oneShareRoundTripCharges <= 0m) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Round-trip charge estimation is unavailable; no order was placed." });
            quantity = expectedProfitPerShare > 0
                ? (int)Math.Min(int.MaxValue, Math.Max(1m, Math.Ceiling(oneShareRoundTripCharges / expectedProfitPerShare)))
                : 0;
            quantity = Math.Min(quantity, maximumAffordable);
            var coversRoundTripCharges = false;
            for (var attempt = 0; attempt < 25 && quantity > 0 && quantity <= maximumAffordable; attempt++)
            {
                var charges = await EstimateRoundTripCharges(stock, quantity, stock.CurrentPrice);
                if (charges <= 0m) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Round-trip charge estimation is unavailable; no order was placed." });
                if (stock.CurrentPrice * quantity * configuration.MinimumBuyProfitChangePercent / 100m > charges) { coversRoundTripCharges = true; break; }
                quantity++;
            }
            if (!coversRoundTripCharges) quantity = 0;
        }
        if (quantity < 1)
        {
            var message = buyMode == "minimum"
                ? "No affordable quantity covers estimated round-trip charges at the configured minimum price change."
                : "The available balance and per-trade allocation cannot fund one share including estimated entry charges.";
            return BadRequest(new { success = false, message, balance });
        }

        if (configuration.PaperTrading || configuration.PaperTradingAsLive)
        {
            var estimate = await EstimateCharges(stock, quantity, stock.CurrentPrice);
            if (estimate <= 0m) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Broker charge estimation is unavailable; no order was placed." });
            var required = stock.CurrentPrice * quantity + estimate;
            if (!_paperPortfolio.TryReserveCapital(required)) return BadRequest(new { success = false, message = "Insufficient paper balance including estimated charges.", required, balance });
            var paperTrade = new TradeOrder
            {
                Symbol = stock.Symbol, SymbolToken = stock.SymbolToken, Exchange = stock.Exchange,
                InstrumentType = InstrumentType.Equity, Direction = TradeDirection.Long,
                ExecutionMode = TradeExecutionMode.Paper, ProductType = configuration.Equity.ProductType,
                OrderType = configuration.Equity.OrderType, Duration = configuration.Equity.Duration,
                Quantity = quantity, EntryPrice = stock.CurrentPrice, CurrentPrice = stock.CurrentPrice,
                HighestPrice = stock.CurrentPrice, LowestPrice = stock.CurrentPrice,
                EntryTime = _clock.Now, Status = TradeStatus.Open, EstimatedCharges = estimate,
                EstimatedEntryCharges = estimate, ReservedCapital = required, EntryReason = "Manual dashboard buy"
            };
            _trades.Add(paperTrade);
            await _trades.SaveAsync(paperTrade);
            _watchList.MarkOwned(stock.SymbolToken, quantity, stock.CurrentPrice, stopLoss: 0m);
            return Ok(new { success = true, mode = configuration.PaperTradingAsLive ? "paper-as-live" : "paper", symbol = stock.Symbol, quantity, price = stock.CurrentPrice, estimatedCharges = estimate, required });
        }

        var liveEntryCharges = await EstimateCharges(stock, quantity, stock.CurrentPrice);
        if (liveEntryCharges <= 0m) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Broker charge estimation is unavailable; no order was placed." });
        var liveRequired = stock.CurrentPrice * quantity + liveEntryCharges;
        if (liveRequired > balance)
            return BadRequest(new { success = false, message = "Insufficient live balance including estimated charges.", required = liveRequired, balance });

        var order = await _angel.PlaceOrderAsync(new PlaceOrderRequest
        {
            TradingSymbol = stock.Symbol, SymbolToken = stock.SymbolToken, Exchange = stock.Exchange,
            TransactionType = "BUY", Quantity = quantity,
            ProductType = configuration.Equity.ProductType, OrderType = configuration.Equity.OrderType,
            Duration = configuration.Equity.Duration, OrderTag = "MANUALBUY"
        }, forceLive: true);
        if (!order.Status) return BadRequest(new { success = false, message = order.Message });
        var liveTrade = new TradeOrder
        {
            Symbol = stock.Symbol, SymbolToken = stock.SymbolToken, Exchange = stock.Exchange,
            InstrumentType = InstrumentType.Equity, Direction = TradeDirection.Long,
            ExecutionMode = TradeExecutionMode.Live, ProductType = configuration.Equity.ProductType,
            OrderType = configuration.Equity.OrderType, Duration = configuration.Equity.Duration,
            Quantity = quantity, EntryPrice = stock.CurrentPrice, CurrentPrice = stock.CurrentPrice,
            HighestPrice = stock.CurrentPrice, LowestPrice = stock.CurrentPrice, EntryTime = _clock.Now,
            BuyOrderId = order.Data?.OrderId ?? "", Status = TradeStatus.PendingBrokerConfirmation,
            EstimatedCharges = liveEntryCharges, EstimatedEntryCharges = liveEntryCharges,
            EntryReason = "Manual dashboard buy"
        };
        _trades.Add(liveTrade);
        await _trades.SaveAsync(liveTrade);
        return Ok(new { success = true, mode = "live", orderId = liveTrade.BuyOrderId, symbol = stock.Symbol, quantity, price = stock.CurrentPrice, estimatedCharges = liveEntryCharges, required = liveRequired });
    }

    [HttpPost("sell")]
    public async Task<IActionResult> Sell([FromBody] ManualTradeRequest request)
    {
        if (!_clock.IsMarketOpen) return BadRequest(new { success = false, message = "Selling is available only during market hours." });
        var trade = _trades.GetByIdentity(request.Exchange, request.SymbolToken);
        if (trade is null || trade.Closed) return NotFound(new { success = false, message = "No open trade was found for this stock." });
        var stock = _watchList.Current.FirstOrDefault(x => string.Equals(x.SymbolToken, request.SymbolToken, StringComparison.OrdinalIgnoreCase));
        var price = stock?.CurrentPrice ?? _marketData.Get(request.SymbolToken)?.Ltp ?? trade.CurrentPrice;
        if (price <= 0) return BadRequest(new { success = false, message = "A valid live price is not available." });

        if (trade.ExecutionMode == TradeExecutionMode.Paper)
        {
            var exitCharges = await EstimateCharges(stock ?? new Gainer { Symbol = trade.Symbol, SymbolToken = trade.SymbolToken, Exchange = trade.Exchange, ProductType = trade.ProductType }, trade.Quantity, price, "SELL");
            if (exitCharges <= 0m) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { success = false, message = "Exit charge estimation is unavailable; the paper position remains open." });
            trade.ExitPrice = price;
            trade.CurrentPrice = price;
            trade.ExitTime = _clock.Now;
            trade.ExitReason = "Manual dashboard sell";
            trade.RealizedGrossProfit = (price - trade.EntryPrice) * trade.Quantity;
            trade.RealizedCharges = trade.EstimatedCharges + exitCharges;
            trade.RealizedNetProfit = trade.RealizedGrossProfit - trade.RealizedCharges;
            _paperPortfolio.CreditCapital(price * trade.Quantity - exitCharges);
            await _trades.CloseAsync(trade);
            return Ok(new { success = true, mode = _settings.Current.PaperTradingAsLive ? "paper-as-live" : "paper", symbol = trade.Symbol, quantity = trade.Quantity, price, netProfit = trade.RealizedNetProfit });
        }

        var sellOrder = await _angel.PlaceOrderAsync(new PlaceOrderRequest
        {
            TradingSymbol = trade.Symbol, SymbolToken = trade.SymbolToken, Exchange = trade.Exchange,
            TransactionType = "SELL", Quantity = trade.Quantity, ProductType = trade.ProductType,
            OrderType = "MARKET", Duration = trade.Duration, IsExitOrder = true,
            OrderTag = $"MANSELL-{Guid.NewGuid():N}"[..19]
        }, forceLive: true);
        if (!sellOrder.Status) return BadRequest(new { success = false, message = sellOrder.Message });
        trade.ExitOrderPlaced = true;
        trade.ExitOrderPlacedAt = DateTime.UtcNow;
        trade.ExitOrderId = sellOrder.Data?.OrderId ?? string.Empty;
        trade.ExitReason = "Manual dashboard sell pending broker confirmation";
        await _trades.UpdateAsync(trade);
        return Accepted(new { success = true, mode = "live", orderId = trade.ExitOrderId, symbol = trade.Symbol, quantity = trade.Quantity, referencePrice = price, message = "Sell order submitted; final P/L will update after broker confirmation." });
    }

    private async Task PopulateExecutionEstimatesAsync(Gainer stock)
    {
        if (stock.CurrentPrice <= 0) return;
        try
        {
            var balance = _settings.Current.PaperTrading || _settings.Current.PaperTradingAsLive
                ? await _paperPortfolio.GetAvailableBalanceAsync()
                : await _angel.GetAvailableBalanceAsync(forceLive: true);
            var expectedProfitPerShare = stock.CurrentPrice * _settings.Current.MinimumBuyProfitChangePercent / 100m;
            var maximumAffordable = (int)Math.Min(int.MaxValue, Math.Max(0m, Math.Floor(balance / stock.CurrentPrice)));
            if (expectedProfitPerShare <= 0 || maximumAffordable < 1)
            {
                stock.MinimumBuyQuantity = 0;
                stock.EstimatedCharges = 0;
                stock.MinimumInvestmentWithCharges = 0;
                stock.SufficientFunds = false;
                return;
            }

            var oneShareCharges = await EstimateRoundTripCharges(stock, 1, stock.CurrentPrice);
            if (oneShareCharges <= 0m)
                throw new InvalidOperationException("Round-trip charge estimate was unavailable.");

            var quantity = Math.Min(maximumAffordable, (int)Math.Min(int.MaxValue, Math.Max(1m, Math.Ceiling(oneShareCharges / expectedProfitPerShare))));
            var charges = 0m;
            var coversCharges = false;
            for (var attempt = 0; attempt < 25 && quantity <= maximumAffordable; attempt++)
            {
                charges = await EstimateRoundTripCharges(stock, quantity, stock.CurrentPrice);
                if (charges <= 0m)
                    throw new InvalidOperationException("Round-trip charge estimate was unavailable.");
                if (quantity * expectedProfitPerShare > charges)
                {
                    coversCharges = true;
                    break;
                }
                quantity++;
            }

            if (!coversCharges || quantity > maximumAffordable)
            {
                stock.MinimumBuyQuantity = 0;
                stock.EstimatedCharges = 0;
                stock.MinimumInvestmentWithCharges = 0;
                stock.SufficientFunds = false;
                return;
            }

            stock.MinimumBuyQuantity = quantity;
            stock.EstimatedCharges = charges;
            stock.MinimumInvestmentWithCharges = stock.CurrentPrice * quantity + charges;
            stock.SufficientFunds = stock.MinimumInvestmentWithCharges <= balance;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not calculate dashboard buy estimates for {Symbol}.", stock.Symbol);
            stock.MinimumBuyQuantity = 0;
            stock.EstimatedCharges = 0;
            stock.MinimumInvestmentWithCharges = 0;
            stock.SufficientFunds = false;
        }
    }

    private async Task<decimal> EstimateRoundTripCharges(Gainer stock, int quantity, decimal price)
    {
        var buy = await EstimateCharges(stock, quantity, price);
        try
        {
            var result = await _angel.EstimateChargesAsync(new BrokerageEstimateRequest
            {
                Orders = [new BrokerageEstimateOrder { ProductType = stock.ProductType, TransactionType = "SELL", Quantity = quantity.ToString(), Price = price.ToString(System.Globalization.CultureInfo.InvariantCulture), Exchange = stock.Exchange, SymbolName = stock.Symbol, Token = stock.SymbolToken }]
            });
            if (!result.Status) return 0m;
            var sellCharges = result.Data?.Summary?.Breakup?.Sum(x => x.Amount) ?? 0m;
            return buy > 0m && sellCharges > 0m ? buy + sellCharges : 0m;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to estimate round-trip charges for manual trade {Symbol}.", stock.Symbol);
            return 0m;
        }
    }

    private async Task<decimal> EstimateCharges(Gainer stock, int quantity, decimal price, string transactionType = "BUY")
    {
        try
        {
            var result = await _angel.EstimateChargesAsync(new BrokerageEstimateRequest
            {
                Orders = [new BrokerageEstimateOrder { ProductType = stock.ProductType, TransactionType = transactionType, Quantity = quantity.ToString(), Price = price.ToString(System.Globalization.CultureInfo.InvariantCulture), Exchange = stock.Exchange, SymbolName = stock.Symbol, Token = stock.SymbolToken }]
            });
            if (!result.Status) return 0m;
            return result.Data?.Summary?.Breakup?.Sum(x => x.Amount) ?? 0m;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to estimate charges for manual trade {Symbol}.", stock.Symbol);
            return 0m;
        }
    }

    private static DateTime ToIndiaTime(DateTime value)
    {
        // Broker historical candle timestamps without an offset represent exchange-local IST.
        if (value.Kind == DateTimeKind.Unspecified) return value;
        if (value.Kind == DateTimeKind.Local) value = value.ToUniversalTime();
        return TimeZoneInfo.ConvertTimeFromUtc(value, MarketClock.IndiaTimeZone);
    }
}

public sealed record MarketCandleResponse(DateTimeOffset Timestamp, decimal Open, decimal High, decimal Low, decimal Close, decimal Volume);
public sealed record ManualTradeRequest(string SymbolToken, int Quantity = 1, string BuyMode = "minimum", string Exchange = "NSE");
public sealed record SendMarketEmailRequest(string Subject, string Message, bool IsHtml = false);
