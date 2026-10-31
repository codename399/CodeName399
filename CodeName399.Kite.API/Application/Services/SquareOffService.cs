using CodeName399.Kite.API.Application.Services;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Services;

public sealed class SquareOffService
{
    private readonly CommonService _commonService;
    private readonly IAngelOneService _angelOneService;
    private readonly IInstrumentService _instrumentService;
    private readonly TradingSettingsProvider _settings;
    private readonly MarketClock _marketClock;
    private readonly ILogger<SquareOffService> _logger;

    public SquareOffService(
        CommonService commonService,
        IAngelOneService angelOneService,
        IInstrumentService instrumentService,
        TradingSettingsProvider settings,
        MarketClock marketClock,
        ILogger<SquareOffService> logger)
    {
        _commonService = commonService;
        _angelOneService = angelOneService;
        _instrumentService = instrumentService;
        _settings = settings;
        _marketClock = marketClock;
        _logger = logger;
    }

    /// <summary>
    /// Sends every owned position through CommonService.PlaceSellOrder, bypassing
    /// the normal price-change threshold. No new model/configuration fields are needed.
    /// </summary>
    public async Task ExecuteAtMarketCloseAsync(CancellationToken token = default)
    {
        if (!_marketClock.IsTradingDay || !_settings.Current.AutoSquareOff)
            return;

        token.ThrowIfCancellationRequested();

        var ownedStocks = await _commonService.GetOwnedStocksAsync();
        if (ownedStocks.Count == 0)
        {
            _logger.LogInformation("Square-off skipped: no owned positions.");
            return;
        }

        var login = await _angelOneService.GetLoginDataAsync();
        if (login == null || string.IsNullOrWhiteSpace(login.JwtToken))
        {
            _logger.LogWarning("Square-off skipped: Angel One login/JWT is unavailable.");
            return;
        }

        var instruments = await _instrumentService.LoadInstruments();
        var now = _marketClock.Now;
        var fromDate = new DateTime(now.Year, now.Month, now.Day, 9, 15, 0, DateTimeKind.Unspecified);

        var candles = await _commonService.DownloadCandlesInBatchesAsync(
            ownedStocks.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase),
            instruments,
            login.JwtToken,
            _settings.Current.CandleInterval,
            fromDate,
            now,
            _settings.Current.CandleBatchSize,
            _settings.Current.BatchDelay);

        token.ThrowIfCancellationRequested();
        await _commonService.PlaceSellOrder(ownedStocks, candles, forceSquareOff: true);

        _logger.LogInformation(
            "Square-off sell pipeline completed for {PositionCount} owned positions.",
            ownedStocks.Count);
    }
}
