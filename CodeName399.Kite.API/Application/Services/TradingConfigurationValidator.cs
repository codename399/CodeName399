using CodeName399.Kite.API.Models.Configuration;

namespace CodeName399.Kite.API.Services;

public static class TradingConfigurationValidator
{
    public static void Validate(TradingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (!configuration.IgnoreMarketHours && configuration.MarketOpenTime >= configuration.MarketCloseTime)
            throw new ArgumentException("MarketOpenTime must be earlier than MarketCloseTime.");
        if (configuration.GainersPageSize is < 1 or > 100)
            throw new ArgumentException("GainersPageSize must be between 1 and 100.");
        if (configuration.WatchListRefreshMinutes < 1)
            throw new ArgumentException("WatchListRefreshMinutes must be at least 1.");
        if (configuration.MinimumBuyProfitChangePercent <= 0m || configuration.MinimumBuyProfitChangePercent > 100m)
            throw new ArgumentException("MinimumBuyProfitChangePercent must be greater than 0 and no more than 100%.");
        if (configuration.MaxCapitalPerTradePercent < 0m || configuration.MaxCapitalPerTradePercent > 100m)
            throw new ArgumentException("MaxCapitalPerTradePercent must be between 0 and 100.");

        var raw = configuration.RawMarketData ?? new RawMarketDataSettings();
        if (string.IsNullOrWhiteSpace(raw.StoragePath)) throw new ArgumentException("RawMarketData.StoragePath is required.");
        if (raw.MaximumZipSizeMb <= 0) throw new ArgumentException("RawMarketData.MaximumZipSizeMb must be positive.");
        if (raw.RollOverAtPercent <= 0 || raw.RollOverAtPercent >= 100) throw new ArgumentException("RawMarketData.RollOverAtPercent must be between 1 and 99.");
    }
}
