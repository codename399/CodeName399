using System.Collections.Concurrent;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.SignalRHubs;
using CodeName399.Kite.API.WebSocket.Interface;
using Microsoft.AspNetCore.SignalR;

namespace CodeName399.Kite.API.Application.Services;

/// <summary>
/// Maintains the latest quote snapshot and forwards broker ticks to connected clients.
/// </summary>
public sealed class MarketQuotePublisher : BackgroundService
{
    private readonly IAngelWebSocketClient _webSocket;
    private readonly IHubContext<MarketHub> _hub;
    private readonly ILogger<MarketQuotePublisher> _logger;
    private readonly ConcurrentDictionary<string, MarketQuoteData> _quotes =
        new(StringComparer.OrdinalIgnoreCase);

    public MarketQuotePublisher(
        IAngelWebSocketClient webSocket,
        IHubContext<MarketHub> hub,
        ILogger<MarketQuotePublisher> logger)
    {
        _webSocket = webSocket;
        _hub = hub;
        _logger = logger;
    }

    public IReadOnlyCollection<MarketQuoteData> GetSnapshot()
        => _quotes.Values.OrderBy(x => x.TradingSymbol).ToArray();

    public async Task PublishSnapshotAsync(IEnumerable<MarketQuoteData> quotes)
    {
        var incoming = (quotes ?? [])
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SymbolToken))
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);

        foreach (var key in _quotes.Keys.ToArray())
        {
            if (!incoming.ContainsKey(key)
                && _quotes.TryGetValue(key, out var existing)
                && existing.Quantity <= 0)
                _quotes.TryRemove(key, out _);
        }

        foreach (var quote in incoming.Values)
        {
            _quotes.AddOrUpdate(
                quote.SymbolToken,
                quote,
                (_, existing) => MergeQuote(existing, quote));
        }

        await _hub.Clients.All.SendAsync("QuotesUpdated", GetSnapshot());
    }

    public async Task SeedOwnedAsync(IEnumerable<TradeOrder> trades)
    {
        foreach (var trade in trades ?? [])
        {
            if (trade == null || string.IsNullOrWhiteSpace(trade.SymbolToken))
                continue;

            _quotes.AddOrUpdate(
                trade.SymbolToken,
                _ => new MarketQuoteData
                {
                    SymbolToken = trade.SymbolToken,
                    TradingSymbol = trade.Symbol,
                    Exchange = trade.Exchange,
                    Quantity = trade.Quantity,
                    Ltp = trade.EntryPrice,
                    TotalInvestment = trade.TotalInvestment,
                    StopLoss = trade.StopLoss,
                    ProductType = "INTRADAY"
                },
                (_, quote) =>
                {
                    quote.Quantity = trade.Quantity;
                    quote.TotalInvestment = trade.TotalInvestment;
                    quote.StopLoss = trade.StopLoss;
                    if (quote.Ltp <= 0) quote.Ltp = trade.EntryPrice;
                    return quote;
                });
        }

        await _hub.Clients.All.SendAsync("QuotesUpdated", GetSnapshot());
    }

    public async Task SetOwnedAsync(
        string symbolToken,
        int quantity,
        decimal totalInvestment = 0m,
        decimal stopLoss = 0m)
    {
        if (string.IsNullOrWhiteSpace(symbolToken))
            return;

        if (_quotes.TryGetValue(symbolToken, out var quote))
        {
            quote.Quantity = Math.Max(0, quantity);
            quote.TotalInvestment = quantity > 0 ? totalInvestment : 0m;
            quote.StopLoss = quantity > 0 ? stopLoss : 0m;
            await _hub.Clients.All.SendAsync("QuotesUpdated", GetSnapshot());
        }
    }

    private async Task HandleTickAsync(MarketTick tick)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tick.SymbolToken))
                return;

            var quote = _quotes.AddOrUpdate(
                tick.SymbolToken,
                _ => CreateQuote(tick),
                (_, existing) => ApplyTick(existing, tick));

            await _hub.Clients.All.SendAsync("StockTickUpdated", quote);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish live market tick. Token={Token}", tick.SymbolToken);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _webSocket.TickReceived += OnTickReceived;
        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            _webSocket.TickReceived -= OnTickReceived;
        }
    }

    private void OnTickReceived(object? sender, MarketTick tick)
    {
        if (tick != null)
            _ = HandleTickAsync(tick);
    }

    private static MarketQuoteData CreateQuote(MarketTick tick)
        => ApplyTick(new MarketQuoteData
        {
            SymbolToken = tick.SymbolToken,
            TradingSymbol = tick.TradingSymbol,
            Exchange = tick.ExchangeType == 3 ? "BSE" : "NSE"
        }, tick);

    private static MarketQuoteData ApplyTick(MarketQuoteData quote, MarketTick tick)
    {
        quote.TradingSymbol = string.IsNullOrWhiteSpace(tick.TradingSymbol)
            ? quote.TradingSymbol
            : tick.TradingSymbol;
        quote.Exchange = tick.ExchangeType == 3 ? "BSE" : "NSE";
        if (tick.Ltp > 0) quote.Ltp = tick.Ltp;
        quote.Bid = tick.Bid;
        quote.Ask = tick.Ask;
        if (tick.Open > 0) quote.Open = tick.Open;
        if (tick.High > 0) quote.High = tick.High;
        if (tick.Low > 0) quote.Low = tick.Low;
        if (tick.Close > 0) quote.Close = tick.Close;
        quote.LastTradedQuantity = tick.LastTradedQuantity;
        if (tick.AveragePrice > 0) quote.AveragePrice = tick.AveragePrice;
        quote.DayVolume = tick.DayVolume;
        quote.Volume = tick.Volume > 0 ? tick.Volume : tick.DayVolume;
        if (tick.PreviousClose > 0) quote.PreviousClose = tick.PreviousClose;
        quote.QuoteTimeUtc = tick.LastUpdated == default ? DateTime.UtcNow : tick.LastUpdated;
        quote.PercentChange = quote.PreviousClose > 0
            ? (quote.Ltp - quote.PreviousClose) / quote.PreviousClose * 100m
            : 0m;
        return quote;
    }

    private static MarketQuoteData MergeQuote(MarketQuoteData existing, MarketQuoteData incoming)
    {
        // Keep the live values already received while refreshing scanner-derived indicators.
        var latest = existing.QuoteTimeUtc > incoming.QuoteTimeUtc ? existing : incoming;
        incoming.Ltp = latest.Ltp > 0 ? latest.Ltp : incoming.Ltp;
        incoming.Bid = latest.Bid > 0 ? latest.Bid : incoming.Bid;
        incoming.Ask = latest.Ask > 0 ? latest.Ask : incoming.Ask;
        incoming.Quantity = existing.Quantity > 0 ? existing.Quantity : incoming.Quantity;
        incoming.QuoteTimeUtc = latest.QuoteTimeUtc;
        return incoming;
    }
}
