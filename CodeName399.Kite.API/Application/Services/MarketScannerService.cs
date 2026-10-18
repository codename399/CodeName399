using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Configuration;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Services.Indicator;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Services;

public sealed class MarketScannerService
{
    private readonly TradingSettingsProvider _settings;
    private readonly TradeManager _tradeManager;
    private readonly IIndicatorService _indicatorService;
    private readonly ILogger<MarketScannerService> _logger;
    private readonly HistoricalDownloadQueue _historyQueue;
    private readonly HistoricalCacheService _historicalCache;

    public MarketScannerService(
        TradeManager tradeManager,
        TradingSettingsProvider settings,
        IIndicatorService indicatorService,
        HistoricalDownloadQueue historyQueue,
        HistoricalCacheService historicalCache,
        ILogger<MarketScannerService> logger)
    {
        _tradeManager = tradeManager;
        _settings = settings;
        _indicatorService = indicatorService;
        _historyQueue = historyQueue;
        _historicalCache = historicalCache;
        _logger = logger;
    }

    public async Task<List<Gainer>> BuildAsync(
        List<MarketQuoteData> stocks,
        string jwtToken,
        HashSet<string> ownedSymbols)
    {
        using var activity = TradingFlowActivity.Start("trading.market-scan");
        var settings = _settings.Current;
        var equity = settings.Equity;

        var tasks = stocks.Select(async quote =>
        {
            var symbol = (quote.TradingSymbol ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(symbol)) return null;

            var trade = _tradeManager.GetByIdentity(quote.Exchange, quote.SymbolToken, symbol);
            var owned = (trade != null && !trade.Closed)
                        || ownedSymbols.Contains(symbol)
                        || _tradeManager.ContainsPosition(quote.Exchange, quote.SymbolToken);

            var indicator = await _indicatorService.GetAsync(quote, jwtToken);
            var gainer = CreateGainer(quote, trade, owned, equity, indicator != null);
            if (indicator != null)
            {
                IndicatorMapper.ApplyIndicators(gainer, indicator);
                gainer.HistoricalLoaded = true;
                gainer.LastHistoricalUpdate = DateTime.UtcNow;
            }
            else if (!_historicalCache.TryGet(quote.SymbolToken, out _)
                     && !_historyQueue.IsQueued(quote.SymbolToken))
            {
                _historicalCache.AddPending(quote.SymbolToken);
                _historyQueue.Enqueue(quote.SymbolToken, CalculateInitialPriority(quote));
            }

            // Historical/indicator availability is research context, not an entry gate.
            gainer.IsSubscribed = true;
            gainer.Signal = owned ? "HOLD" : "BUY";
            gainer.Reason = owned ? "Position Open" : "Risk/charges validation only.";
            gainer.Direction = TradeDirection.Long;
            gainer.Risk = "RISK";

            if (trade != null)
            {
                gainer.StopLoss = trade.StopLoss;
                gainer.IsOwned = true;
                gainer.Signal = "HOLD";
                gainer.Quantity = trade.Quantity;
                gainer.AveragePrice = trade.EntryPrice;
            }

            gainer.Priority = CalculateInitialPriority(quote);
            return gainer;
        });

        var result = (await Task.WhenAll(tasks))
            .Where(x => x != null)
            .Cast<Gainer>()
            .OrderByDescending(x => x.IsOwned)
            .ThenByDescending(x => x.Priority)
            .ThenByDescending(x => x.Signal == "BUY")
            .ThenByDescending(x => x.ChangePercent)
            .ToList();

        return result;
    }

    public bool IsExcluded(string symbol) => _settings.Current.ExcludedSymbols.Any(x =>
        x.Equals(symbol, StringComparison.OrdinalIgnoreCase));

    private static Gainer CreateGainer(MarketQuoteData quote, TradeOrder? trade, bool owned, InstrumentTradingSettings settings, bool loaded)
    {
        var change = quote.Close <= 0m ? 0m : Math.Round((quote.Ltp - quote.Close) / quote.Close * 100m, 2);
        return new Gainer
        {
            Symbol = (quote.TradingSymbol ?? string.Empty).Trim(),
            SymbolToken = quote.SymbolToken,
            InstrumentType = quote.InstrumentType,
            Direction = TradeDirection.Long,
            Exchange = quote.Exchange,
            ProductType = quote.ProductType,
            OrderType = settings.OrderType,
            Duration = settings.Duration,
            Bid = quote.Bid,
            Ask = quote.Ask,
            Spread = quote.Spread,
            SpreadPercent = quote.SpreadPercent,
            CurrentPrice = quote.Ltp,
            PreviousClose = quote.Close,
            ChangePercent = change,
            Quote = quote,
            IsOwned = owned,
            HistoricalLoaded = loaded,
            LastTickUpdated = quote.QuoteTimeUtc,
            StopLoss = trade?.StopLoss ?? 0m,
            Quantity = trade?.Quantity ?? 0,
            AveragePrice = trade?.EntryPrice ?? 0m,
            UpperCircuitLimit = quote.UpperCircuitLimit,
            LowerCircuitLimit = quote.LowerCircuitLimit,
            InvestedAmount = trade == null ? 0m : trade.EntryPrice * trade.Quantity,
            CurrentValue = trade == null ? 0m : quote.Ltp * trade.Quantity,
            ProfitLoss = trade == null ? 0m : (quote.Ltp - trade.EntryPrice) * trade.Quantity,
            ProfitLossPercentage = trade == null || trade.EntryPrice <= 0m ? 0m : Math.Round((quote.Ltp - trade.EntryPrice) / trade.EntryPrice * 100m, 2)
        };
    }

    private static int CalculateInitialPriority(MarketQuoteData quote)
    {
        var priority = 1;
        if (quote.TradeVolume > 0) priority += 10;
        if (quote.Close > 0 && quote.Ltp > quote.Close)
            priority += (int)Math.Min(((quote.Ltp - quote.Close) / quote.Close) * 500m, 50m);
        return Math.Max(priority, 1);
    }
}
