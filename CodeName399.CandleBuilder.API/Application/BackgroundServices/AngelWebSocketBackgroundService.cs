using CodeName399.CandleBuilder.API.Constants;
using CodeName399.CandleBuilder.API.Extensions;
using CodeName399.CandleBuilder.API.Helpers;
using CodeName399.CandleBuilder.API.Models;
using CodeName399.CandleBuilder.API.Services.Interface;
using CodeName399.CandleBuilder.API.WebSocket.Interface;
using CodeName399.Kite.API.Models;
using Microsoft.Extensions.Options;

namespace CodeName399.CandleBuilder.API.Application.BackgroundServices;

/// <summary>Loads the equity master on startup and establishes the live feed subscription.</summary>
public sealed class AngelWebSocketBackgroundService : BackgroundService
{
    private readonly IAngelWebSocketClient _socket;
    private readonly IInstrumentService _instruments;
    private readonly IAngelOneService _angelOneService;
    private readonly AngelOneConfiguration _options;
    private readonly RequestHelper _requestHelper;
    private readonly ILogger<AngelWebSocketBackgroundService> _logger;
    public List<string> ExcludedSymbols =
            new List<string> { "TATSILV-EQ", "AONEGOLD-EQ", "ACUTAAS-EQ", "IDEA-EQ" };
    private readonly SemaphoreSlim _quoteSemaphore = new(2, 2);
    public AngelWebSocketBackgroundService(IAngelWebSocketClient socket, IInstrumentService instruments, IAngelOneService angelOneService,IOptions<AngelOneConfiguration> options,
        RequestHelper requestHelper, ILogger<AngelWebSocketBackgroundService> logger)
    {
        _socket = socket;
        _instruments = instruments;
        _angelOneService = angelOneService;
        _options = options.Value;
        _requestHelper = requestHelper;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!_socket.IsConnected) await _socket.StartAsync(stoppingToken);
                var loginData = await _angelOneService.GetLoginDataAsync();
                var equity = await _instruments.LoadInstruments();
                var quotes = GetTopQuotes(await GetQuotesAsync(equity, loginData.JwtToken), 1000);
                foreach (var batch in quotes.Chunk(100))
                {
                    await _socket.SubscribeAsync(batch.Select(x => x.SymbolToken), batch.First().Exchange);
                    await Task.Delay(100, stoppingToken);
                }
                _logger.LogInformation(
                    "Subscribed to {Count} equity instruments. The instrument/quote scan will run again in 5 minutes.",
                    quotes.Count);

                // Refresh the instrument quote ranking and websocket subscriptions every 5 minutes.
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Market websocket startup/recovery failed."); await Task.Delay(5000, stoppingToken); }
        }
    }

    public long GetActivityVolume(MarketQuoteData quote) =>
    Math.Max(quote.TradeVolume, Math.Max(quote.DayVolume, Math.Max(quote.Volume, quote.LastTradedQuantity)));

    public async Task<List<MarketQuoteData>> GetQuotesAsync(
        List<Instrument> instruments,
        string jwt)
    {
        var excluded = (ExcludedSymbols ?? [])
            .Select(x => x.ToUpperInvariant())
            .ToHashSet();

        var quotes = await GetQuotesInChunksAsync(instruments, jwt);
        if (excluded.Count > 0)
            quotes = quotes.Where(q => !excluded.Contains((q.TradingSymbol ?? string.Empty).ToUpperInvariant())).ToList();

        return quotes;
    }

    private async Task<List<MarketQuoteData>> GetQuotesInChunksAsync(List<Instrument> instruments, string jwt)
    {
        // Quote requests were previously issued one 50-token chunk at a time.
        // With a full NSE equity master this consumed most of the minute and
        // caused the next buy evaluation to miss a candle close. Keep a strict
        // concurrency limit of two (also enforced by _quoteSemaphore), while
        // allowing independent exchange/token chunks to overlap.
        var requests = instruments
            .Where(x => !string.IsNullOrWhiteSpace(x.token) && !string.IsNullOrWhiteSpace(x.exch_seg))
            .GroupBy(x => x.exch_seg.Trim(), StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => ChunkTokens(group.Select(x => x.token.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList())
                .Select(batch => (Exchange: group.Key, Tokens: batch)))
            .ToList();

        var quotes = new List<MarketQuoteData>();
        foreach (var request in requests)
        {
            // A provider 403 activates the shared endpoint cooldown in RequestHelper.
            // Stop issuing quote requests for the rest of this scan instead of
            // queuing dozens of requests that cannot succeed during the cooldown.
            if (RequestHelper.IsEndpointCoolingDown(AngelApiUrlConstant.marketDataUrl))
            {
                _logger.LogWarning(
                    "QUOTE SCAN PAUSED: Angel One market-quote endpoint is cooling down. RemainingBatches={RemainingBatches}",
                    requests.Count - requests.IndexOf(request));
                break;
            }

            var batch = await GetBatchAsync(request.Tokens, jwt, request.Exchange);
            quotes.AddRange(batch);
        }

        return quotes;
    }

    private IEnumerable<List<string>> ChunkTokens(List<string> tokens, int size = 50)
    {
        for (var i = 0; i < tokens.Count; i += size)
            yield return tokens.Skip(i).Take(size).ToList();
    }

    private async Task<List<MarketQuoteData>> GetBatchAsync(List<string> tokens, string jwt, string exchange)
    {
        await _quoteSemaphore.WaitAsync();
        try
        {
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

    public List<MarketQuoteData> GetTopQuotes(List<MarketQuoteData> quotes, int count)
    {
        return quotes
            .Where(x => x.Close > 0m && x.Ltp > x.Close)
        .Where(x => GetActivityVolume(x) > 0L)
        .OrderByDescending(GetActivityVolume)
        .ThenByDescending(x => (x.Ltp - x.Close) / x.Close)
        .ThenByDescending(x => x.Open > 0m ? (x.Ltp - x.Open) / x.Open : decimal.MinValue)
        .ThenBy(x => x.Ltp)
        .Take(Math.Max(0, count))
        .ToList();
    }
}
