using System.Text.Json;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Application.Services;

/// <summary>Read-only consumer for candle files produced by CandleBuilder.API.</summary>
public sealed class CandleFileStoreService
{
    private readonly ILogger<CandleFileStoreService> _logger;
    private readonly string _directory;
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public CandleFileStoreService(ILogger<CandleFileStoreService> logger)
    {
        _logger = logger;
        var configured = Environment.GetEnvironmentVariable("CODENAME399_CANDLE_STORAGE_PATH");
        if (string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException("CODENAME399_CANDLE_STORAGE_PATH must point to the shared CandleBuilder candle directory.");

        _directory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured.Trim()));
        Directory.CreateDirectory(_directory);

        _logger.LogInformation("CandleBuilder shared candle directory configured (read-only). Directory={CandleDirectory}", _directory);
    }

    public async Task<List<Candle>> GetAsync(string token, string symbol, string interval, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return [];
        var builderInterval = NormalizeInterval(interval);
        var safeToken = string.Concat(token.Trim().Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' ? c : '_'));
        var path = Path.Combine(_directory, $"{safeToken}_{builderInterval}.json");
        if (!File.Exists(path))
        {
            _logger.LogWarning("CANDLEBUILDER FILE NOT FOUND Symbol={Symbol} Token={Token} Interval={Interval} Path={Path}", symbol, token, builderInterval, path);
            return [];
        }

        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
            var candles = await JsonSerializer.DeserializeAsync<List<Candle>>(stream, _json, ct) ?? [];
            var duration = IntervalDuration(builderInterval);
            var nowIst = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ResolveIndiaTimeZone());
            var completedBoundary = nowIst.AddSeconds(-1);
            return candles
                .Where(c => c != null && c.Open > 0m && c.Close > 0m && c.Low > 0m && c.High >= c.Low &&
                            c.Timestamp.Add(duration) <= completedBoundary)
                .GroupBy(c => c.Timestamp).Select(g => g.Last()).OrderBy(c => c.Timestamp).ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unable to read CandleBuilder candle file. Symbol={Symbol} Token={Token} Interval={Interval} Path={Path}", symbol, token, builderInterval, path);
            return [];
        }
    }

    private static string NormalizeInterval(string interval) => interval?.Trim().ToUpperInvariant() switch
    {
        "ONE_MINUTE" or "1MIN" or "1M" => "1m",
        "FIVE_MINUTE" or "5MIN" or "5M" => "5m",
        "THIRTY_MINUTE" or "30MIN" or "30M" => "30m",
        _ => throw new ArgumentException($"Unsupported CandleBuilder interval '{interval}'.", nameof(interval))
    };

    public static TimeSpan IntervalDuration(string interval) => NormalizeInterval(interval) switch
    {
        "1m" => TimeSpan.FromMinutes(1),
        "5m" => TimeSpan.FromMinutes(5),
        "30m" => TimeSpan.FromMinutes(30),
        _ => TimeSpan.FromMinutes(1)
    };

    private static TimeZoneInfo ResolveIndiaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }
}
