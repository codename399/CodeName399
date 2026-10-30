using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Extensions;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Helpers.Calculators;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Services.Interface;
using Microsoft.Extensions.Options;

namespace CodeName399.Kite.API.Services;

public sealed class AngelMarketService : IAngelMarketService
{
    private readonly AngelOneConfiguration _options;
    private readonly IInstrumentService _instrumentService;
    private readonly IAngelOneService _angel;
    private readonly IPortfolioService _portfolio;
    private readonly TradingSettingsProvider _settings;
    private readonly RequestHelper _requestHelper;
    private readonly TradeManager _tradeManager;
    private readonly SmartApiRateLimiter _rateLimiter;
    private readonly SemaphoreSlim _quoteSemaphore = new(2, 2);
    private readonly MarketScannerService _marketScanner;
    private readonly ILogger<AngelMarketService> _logger;

    public AngelMarketService(
        IOptions<AngelOneConfiguration> options,
        IInstrumentService instrumentService,
        IAngelOneService angel,
        IPortfolioService portfolio,
        TradingSettingsProvider settings,
        RequestHelper requestHelper,
        TradeManager tradeManager,
        SmartApiRateLimiter rateLimiter,
        MarketScannerService marketScanner,
        ILogger<AngelMarketService> logger)
    {
        _options = options.Value;
        _instrumentService = instrumentService;
        _angel = angel;
        _portfolio = portfolio;
        _settings = settings;
        _marketScanner = marketScanner;
        _requestHelper = requestHelper;
        _tradeManager = tradeManager;
        _rateLimiter = rateLimiter;
        _logger = logger;
    }

    public async Task<List<Gainer>> GetTopGainersAsync()
    {
        var login = await _angel.GetLoginDataAsync();
        var all = await _instrumentService.LoadInstruments();
        var openTrades = _tradeManager.GetOpenTrades().Where(x => x.InstrumentType == InstrumentType.Equity).ToList();

        var quotes = await GetEquityQuotesAsync(all, login.JwtToken, openTrades);

        var openInstruments = (await Task.WhenAll(openTrades.Select(t =>
            _instrumentService.LoadInstruments())))
            .Where(x => x != null)
            .Cast<Instrument>()
            .ToList();

        var missingOpen = openInstruments
            .Where(i => quotes.All(q => !string.Equals(q.SymbolToken, i.token, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (missingOpen.Count > 0)
            quotes.AddRange(await GetQuotesAsync(missingOpen, login.JwtToken));

        ApplyMetadata(quotes, all.Concat(openInstruments).ToList());

        var holdings = (await _portfolio.GetOwnedSymbolsAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var openTokens = openTrades.Select(x => x.SymbolToken).Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var scannerQuotes = new ScannerQuotes
        {
            Quotes = quotes.Where(q => !openTokens.Contains(q.SymbolToken) && !holdings.Contains(q.TradingSymbol)).ToList(),
            OwnedQuotes = quotes.Where(q => openTokens.Contains(q.SymbolToken) || holdings.Contains(q.TradingSymbol)).ToList()
        };

        var availableCandidateCount = scannerQuotes.Quotes.Count;
        // Shortlist the ten most active equity stocks that are positive for the
        // day and currently trading above their open. Activity is ranked first,
        // with daily gain and intraday rise as deterministic tie-breakers.
        var eligibleQuotes = scannerQuotes.Quotes
            .Where(x => x.Ltp > 0m && x.Close > 0m && x.Ltp > x.Close)
            .Where(x => GetActivityVolume(x) > 0L)
            .ToList();

        scannerQuotes.Quotes = eligibleQuotes
            .OrderByDescending(GetActivityVolume)
            .ThenByDescending(x => (x.Ltp - x.Close) / x.Close)
            .ThenByDescending(x => (x.Ltp - x.Open) / x.Open)
            .Take(10)
            .ToList();

        _logger.LogInformation(
            "TOP10 EQUITY SHORTLIST UniverseQuotes={UniverseQuotes} EligibleQuotes={EligibleQuotes} SelectedTop10={SelectedTop10} PositiveDailyAndRisingFromOpen=true RankedBy=ActivityVolume,DailyChange,IntradayRise",
            availableCandidateCount, eligibleQuotes.Count, scannerQuotes.Quotes.Count);

        scannerQuotes.OwnedQuotes = scannerQuotes.OwnedQuotes
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        var scanned = await _marketScanner.BuildAsync(
            scannerQuotes.Quotes.Concat(scannerQuotes.OwnedQuotes).ToList(),
            login.JwtToken,
            holdings);

        return scanned
            .Where(x => x != null && x.InstrumentType == InstrumentType.Equity)
            .GroupBy(x => x.SymbolToken, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static long GetActivityVolume(MarketQuoteData quote) =>
        Math.Max(quote.TradeVolume, Math.Max(quote.DayVolume, Math.Max(quote.Volume, quote.LastTradedQuantity)));

    private async Task<List<MarketQuoteData>> GetEquityQuotesAsync(
        List<Instrument> all,
        string jwt,
        List<TradeOrder> openTrades)
    {
        var settings = _settings.Current;

        var excluded = (settings.ExcludedSymbols ?? [])
            .Select(x => x.ToUpperInvariant())
            .ToHashSet();

        var exchange = string.IsNullOrWhiteSpace(settings.Equity.Exchange)
            ? "NSE"
            : settings.Equity.Exchange.Trim().ToUpperInvariant();

        var instruments = all
            .Where(x => x.InstrumentType == InstrumentType.Equity)
            .Where(x => string.Equals(x.exch_seg, exchange, StringComparison.OrdinalIgnoreCase))
            .Where(x => !excluded.Contains(x.symbol.ToUpperInvariant()))
            .ToList();

        var quotes = await GetQuotesAsync(instruments, jwt);

        var missingOpen = openTrades
            .Where(x => quotes.All(q => !string.Equals(q.SymbolToken, x.SymbolToken, StringComparison.OrdinalIgnoreCase)))
            .Select(x => all.FirstOrDefault(i =>
                string.Equals(i.exch_seg, x.Exchange, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(i.token, x.SymbolToken, StringComparison.OrdinalIgnoreCase)))
            .Where(x => x != null)
            .Cast<Instrument>()
            .ToList();

        if (missingOpen.Count > 0)
            quotes.AddRange(await GetQuotesAsync(missingOpen, jwt));

        return quotes;
    }

    private async Task<List<MarketQuoteData>> GetQuotesAsync(List<Instrument> instruments, string jwt)
    {
        var result = new List<MarketQuoteData>();
        foreach (var group in instruments.GroupBy(x => x.exch_seg))
        {
            foreach (var batch in IndicatorHelper.ChunkTokens(group.Select(x => x.token).ToList()))
                result.AddRange(await GetBatchAsync(batch, jwt, group.Key));
        }
        return result;
    }

    private async Task<List<MarketQuoteData>> GetBatchAsync(List<string> tokens, string jwt, string exchange)
    {
        await _quoteSemaphore.WaitAsync();
        try
        {
            await _rateLimiter.WaitAsync("quote");
            using var request = new HttpRequestMessage(HttpMethod.Post, AngelApiUrlConstant.marketDataUrl);
            request.AddAngelOneHeaders(_options, jwt);
            request.AddContent(new
            {
                mode = "FULL",
                exchangeTokens = new Dictionary<string, List<string>> { [exchange] = tokens }
            });

            var result = await _requestHelper.SendAsync<MarketQuoteResponse>(request);
            var data = result.Data?.Fetched ?? [];

            foreach (var quote in data)
            {
                if (quote.Bid <= 0)
                    quote.Bid = quote.Depth?.Buy?.Where(x => x.Price > 0).Select(x => x.Price).DefaultIfEmpty(0m).Max() ?? 0m;
                if (quote.Ask <= 0)
                    quote.Ask = quote.Depth?.Sell?.Where(x => x.Price > 0).Select(x => x.Price).DefaultIfEmpty(0m).Min() ?? 0m;

                quote.InstrumentType = InstrumentType.Equity;
                quote.Exchange = exchange;
                quote.QuoteTimeUtc = DateTime.UtcNow;
            }

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Equity quote request failed for {Exchange}", exchange);
            return [];
        }
        finally
        {
            _quoteSemaphore.Release();
        }
    }


    private static void ApplyMetadata(List<MarketQuoteData> quotes, List<Instrument> instruments)
    {
        var byToken = instruments
            .Where(x => !string.IsNullOrWhiteSpace(x.token))
            .GroupBy(x => x.token, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var quote in quotes)
        {
            if (!byToken.TryGetValue(quote.SymbolToken, out var instrument))
                continue;

            quote.InstrumentType = InstrumentType.Equity;
            quote.Exchange = instrument.exch_seg;
            if (string.IsNullOrWhiteSpace(quote.TradingSymbol))
                quote.TradingSymbol = instrument.symbol;
        }
    }
}
