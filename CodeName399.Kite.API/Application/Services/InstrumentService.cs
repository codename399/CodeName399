using CodeName399.Kite.API.Constants;
using CodeName399.Kite.API.Models.Enum;
using CodeName399.Kite.API.Services.Interface;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CodeName399.Kite.API.Services;

public sealed class InstrumentService : IInstrumentService
{
    private readonly HttpClient _httpClient;
    private readonly TradingSettingsProvider _settings;
    private readonly ILogger<InstrumentService> _logger;
    private List<Instrument> _cache = [];
    private DateTime _loadedAt;
    private readonly SemaphoreSlim _loadSemaphore = new(1, 1);
    private readonly ConcurrentDictionary<string, Instrument> _tokenCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Instrument> _symbolCache = new(StringComparer.OrdinalIgnoreCase);

    public InstrumentService(IHttpClientFactory factory, TradingSettingsProvider settings, ILogger<InstrumentService> logger)
    {
        _httpClient = factory.CreateClient();
        _settings = settings;
        _logger = logger;
    }

    private bool IsLoadedForToday() => _cache.Count > 0 && _loadedAt.Date == DateTime.Now.Date;

    private async Task<List<Instrument>> DownloadEquityInstrumentsAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(
                AngelApiUrlConstant.instrumentUrl,
                HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync();
            var instruments = await JsonSerializer.DeserializeAsync<List<Instrument>>(stream) ?? [];

            var equity = instruments
                .Where(x => !string.IsNullOrWhiteSpace(x.token))
                .Where(x => x.exch_seg is "NSE" or "BSE")
                .Where(x => x.symbol.EndsWith("-EQ", StringComparison.OrdinalIgnoreCase))
                .Select(x =>
                {
                    x.InstrumentType = InstrumentType.Equity;
                    return x;
                })
                .ToList();

            if (equity.Count == 0)
                throw new InvalidOperationException("Angel One instrument master contained no equity instruments.");

            _logger.LogInformation("Equity instrument master loaded. Total={Total}, Equity={Equity}", instruments.Count, equity.Count);
            return equity;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Unable to load the equity instrument master.", ex);
        }
    }

    public async Task<List<Instrument>> LoadInstruments()
    {
        if (IsLoadedForToday())
            return _cache;

        await _loadSemaphore.WaitAsync();
        try
        {
            if (IsLoadedForToday())
                return _cache;

            var instruments = await DownloadEquityInstrumentsAsync();
            var tokenCache = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);
            var symbolCache = new Dictionary<string, Instrument>(StringComparer.OrdinalIgnoreCase);

            foreach (var instrument in instruments)
            {
                tokenCache[instrument.token] = instrument;
                if (!string.IsNullOrWhiteSpace(instrument.symbol))
                {
                    symbolCache[instrument.symbol] = instrument;
                    var normalized = instrument.symbol.EndsWith("-EQ", StringComparison.OrdinalIgnoreCase)
                        ? instrument.symbol[..^3]
                        : instrument.symbol;
                    if (!string.IsNullOrWhiteSpace(normalized))
                        symbolCache[normalized] = instrument;
                }
            }

            _tokenCache.Clear();
            foreach (var pair in tokenCache) _tokenCache[pair.Key] = pair.Value;
            _symbolCache.Clear();
            foreach (var pair in symbolCache) _symbolCache[pair.Key] = pair.Value;

            _cache = instruments;
            _loadedAt = DateTime.Now;
            _settings.Current.InstrumentLoadedAt = _loadedAt;

            _logger.LogInformation("Equity instrument cache ready. Count={Count}, LoadedAt={LoadedAt}", _cache.Count, _loadedAt);
            return _cache;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Equity instrument cache load failed. ExistingCount={Count}", _cache.Count);
            if (_cache.Count > 0) return _cache;
            throw;
        }
        finally
        {
            _loadSemaphore.Release();
        }
    }
}