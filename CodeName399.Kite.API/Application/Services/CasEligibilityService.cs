using CodeName399.Kite.API.Services.Interface;
using System.Collections.Concurrent;

namespace CodeName399.Kite.API.Services;

public sealed class CasEligibilityService
{
    private readonly IInstrumentService _instrumentService;
    private readonly ConcurrentDictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);

    public CasEligibilityService(IInstrumentService instrumentService)
    {
        _instrumentService = instrumentService;
    }

    public async Task<bool> IsCasEligibleAsync(string exchange, string symbolToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(symbolToken)) return false;
        if (!exchange.Equals("NSE", StringComparison.OrdinalIgnoreCase)
            && !exchange.Equals("BSE", StringComparison.OrdinalIgnoreCase)) return false;

        if (_cache.TryGetValue($"{exchange}:{symbolToken}", out var cached))
            return cached;

        var instrument = await _instrumentService.LoadInstruments();
        //if (instrument != null && !instrument.exch_seg.Equals(exchange, StringComparison.OrdinalIgnoreCase))
        //    instrument = null;

        var result = true;
        _cache[$"{exchange}:{symbolToken}"] = result;
        return result;
    }
}
