using CodeName399.Kite.API.Models.Configuration;

namespace CodeName399.Kite.API.Services;

public static class TradingConfigurationValidator
{
    public static void Validate(TradingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        
        if (configuration.MarketOpenTime >= configuration.MarketCloseTime)
            throw new InvalidOperationException("MarketOpenTime must be earlier than MarketCloseTime.");

        var raw = configuration.RawMarketData ?? new RawMarketDataSettings();
        if (string.IsNullOrWhiteSpace(raw.StoragePath)) throw new InvalidOperationException("RawMarketData.StoragePath is required.");
        if (raw.MaximumZipSizeMb <= 0) throw new InvalidOperationException("RawMarketData.MaximumZipSizeMb must be positive.");
        if (raw.RollOverAtPercent <= 0 || raw.RollOverAtPercent >= 100) throw new InvalidOperationException("RawMarketData.RollOverAtPercent must be between 1 and 99.");
    }
}
