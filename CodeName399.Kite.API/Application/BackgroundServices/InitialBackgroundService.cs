namespace CodeName399.Kite.API.Application.BackgroundServices;

using CodeName399.Kite.API.Application.Services;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Interface;
using System.Collections.Concurrent;
using System.Text.Json;

/// <summary>Consumes CandleBuilder's completed 1m JSON files. It does not download instruments, quotes or history.</summary>
public sealed class InitialBackgroundService : BackgroundService
{
    private readonly ILogger<InitialBackgroundService> _logger;
    private readonly CommonService _common;
    private readonly IAngelOneService _angel;
    private readonly TradingSettingsProvider _settings;
    private readonly IMarketHoursService _hours;
    private readonly string _directory;
    private readonly ConcurrentDictionary<string, DateTime> _processed = new(StringComparer.OrdinalIgnoreCase);
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public InitialBackgroundService(ILogger<InitialBackgroundService> logger, CommonService common,
        IAngelOneService angel, TradingSettingsProvider settings, IMarketHoursService hours)
    {
        _logger = logger; _common = common; _angel = angel; _settings = settings; _hours = hours;
        var path = Environment.GetEnvironmentVariable("CODENAME399_CANDLE_STORAGE_PATH");
        if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("CODENAME399_CANDLE_STORAGE_PATH must point to CandleBuilder storage.");
        _directory = Path.GetFullPath(path);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Candle directory consumer started. Directory={Directory}; polling every 1 second; timezone=Asia/Kolkata", _directory);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (Directory.Exists(_directory))
                {
                    foreach (var file in Directory.EnumerateFiles(_directory, "*_1m.json"))
                    {
                        stoppingToken.ThrowIfCancellationRequested();
                        var info = new FileInfo(file); var token = Path.GetFileName(file)[..^"_1m.json".Length];
                        if (string.IsNullOrWhiteSpace(token) || (_processed.TryGetValue(token, out var old) && info.LastWriteTimeUtc <= old)) continue;
                        List<Candle> candles;
                        await using (var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, true))
                            candles = await JsonSerializer.DeserializeAsync<List<Candle>>(stream, _json, stoppingToken) ?? [];
                        candles = candles.Where(c => c.Close > 0).GroupBy(c => c.Timestamp).Select(g => g.Last()).OrderBy(c => c.Timestamp).ToList();
                        if (candles.Count < 22) { _processed[token] = info.LastWriteTimeUtc; continue; }
                        var closes = candles.Select(c => c.Close).ToList();
                        var e9 = EmaCalculator.CalculateSeries(closes, 9); var e21 = EmaCalculator.CalculateSeries(closes, 21);
                        var last = candles[^1].Timestamp;
                        var crossed = e9[^2] <= e21[^2] && e9[^1] > e21[^1];
                        _processed[token] = info.LastWriteTimeUtc;
                        _logger.LogInformation("CANDLE CHECK Token={Token} CloseTime={CloseTime:yyyy-MM-dd HH:mm:ss} IST EMA9={EMA9:F6} EMA21={EMA21:F6} Crossed={Crossed}", token, last, Math.Round(e9[^1], 6, MidpointRounding.AwayFromZero), Math.Round(e21[^1], 6, MidpointRounding.AwayFromZero), crossed);
                        if (!crossed || !_hours.IsOpen() || !_hours.IsNewEntryOpen()) continue;
                        var login = await _angel.GetLoginDataAsync();
                        var quote = new MarketQuoteData
                        {
                            Exchange = "NSE",
                            TradingSymbol = token,
                            SymbolToken = token,
                            Ltp = candles[^1].Close,
                            Close = candles[^1].Close,
                            Open = candles[^1].Open,
                            High = candles[^1].High,
                            Low = candles[^1].Low,
                            Ema9 = Math.Round(e9[^1], 6, MidpointRounding.AwayFromZero),
                            Ema21 = Math.Round(e21[^1], 6, MidpointRounding.AwayFromZero),
                            PreviousCandleEma9 = Math.Round(e9[^2], 6, MidpointRounding.AwayFromZero),
                            PreviousCandleEma21 = Math.Round(e21[^2], 6, MidpointRounding.AwayFromZero),
                            QuoteTimeUtc = DateTime.UtcNow
                        };
                        await _common.PlaceBuyOrders([quote], new Dictionary<string, List<Candle>> { [token] = candles }, login.JwtToken);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Candle directory polling failed."); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
