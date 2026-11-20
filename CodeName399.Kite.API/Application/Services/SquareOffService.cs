using CodeName399.Kite.API.Application.Services;
namespace CodeName399.Kite.API.Services;

public sealed class SquareOffService
{
    private readonly CommonService _commonService;
    private readonly TradingSettingsProvider _settings;
    private readonly MarketClock _marketClock;
    private readonly ILogger<SquareOffService> _logger;
public SquareOffService(
        CommonService commonService,
        TradingSettingsProvider settings,
        MarketClock marketClock,
        ILogger<SquareOffService> logger)
    {
        _commonService = commonService;
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

        var interval = string.IsNullOrWhiteSpace(_settings.Current.CandleInterval)
            ? "ONE_MINUTE"
            : _settings.Current.CandleInterval;
        var candles = await _commonService.LoadOwnedCandlesFromCandleBuilderAsync(
            ownedStocks, interval, token);

        token.ThrowIfCancellationRequested();
        await _commonService.PlaceSellOrder(ownedStocks, candles, forceSquareOff: true);

        _logger.LogInformation(
            "Square-off sell pipeline completed for {PositionCount} owned positions.",
            ownedStocks.Count);
    }
}
