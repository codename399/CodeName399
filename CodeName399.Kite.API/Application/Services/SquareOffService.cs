using CodeName399.Kite.API.Application.Services;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Services;

public sealed class SquareOffService
{
    private readonly CommonService _commonService;
    private readonly TradingSettingsProvider _settings;
    private readonly MarketClock _marketClock;
    private readonly ILogger<SquareOffService> _logger;
    private readonly IAngelOneService _angel;
    private readonly IInstrumentService _instrumentService;

    public SquareOffService(
        CommonService commonService,
        TradingSettingsProvider settings,
        MarketClock marketClock,
        ILogger<SquareOffService> logger,
        IAngelOneService angel,
        IInstrumentService instrumentService)
    {
        _commonService = commonService;
        _settings = settings;
        _marketClock = marketClock;
        _logger = logger;
        _angel = angel;
        _instrumentService = instrumentService;
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

        var interval = string.IsNullOrWhiteSpace(_settings.Current.CandleInterval)
            ? "ONE_MINUTE"
            : _settings.Current.CandleInterval;
        var login = await _angel.GetLoginDataAsync();
        var instruments = await _instrumentService.LoadInstruments();
        var candles = await _commonService.DownloadCandlesInBatchesAsync(
            ownedStocks.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase), instruments,
            login.JwtToken, interval, _commonService.GetIndianTime().Date.AddDays(-5),
            _commonService.GetIndianTime(), 2, 1);

        token.ThrowIfCancellationRequested();
        await _commonService.PlaceSellOrder(ownedStocks, candles, forceSquareOff: true);

        _logger.LogInformation(
            "Square-off sell pipeline completed for {PositionCount} owned positions.",
            ownedStocks.Count);
    }
}
