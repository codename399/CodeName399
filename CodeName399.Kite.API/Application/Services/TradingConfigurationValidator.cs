using CodeName399.Kite.API.Models.Configuration;

namespace CodeName399.Kite.API.Services;

public static class TradingConfigurationValidator
{
    public static void Validate(TradingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var e = configuration.Exit ?? new ExitSettings();
        if (e.TrailingStopAtrMultiplier < 0)
            throw new InvalidOperationException("Exit.TrailingStopAtrMultiplier cannot be negative.");
        if (e.TrailingProfitRetentionPercent is < 0 or > 100)
            throw new InvalidOperationException("Exit.TrailingProfitRetentionPercent must be between 0 and 100.");

        if (!configuration.IgnoreMarketHours && configuration.MarketOpenTime >= configuration.MarketCloseTime)
            throw new InvalidOperationException("MarketOpenTime must be earlier than MarketCloseTime.");
        if (configuration.IntradayCutoffTime <= configuration.MarketOpenTime)
            throw new InvalidOperationException("The intraday cutoff (30 minutes before market close) must be after MarketOpenTime.");

        var raw = configuration.RawMarketData ?? new RawMarketDataSettings();
        if (string.IsNullOrWhiteSpace(raw.StoragePath)) throw new InvalidOperationException("RawMarketData.StoragePath is required.");
        if (raw.MaximumZipSizeMb <= 0) throw new InvalidOperationException("RawMarketData.MaximumZipSizeMb must be positive.");
        if (raw.RollOverAtPercent <= 0 || raw.RollOverAtPercent >= 100) throw new InvalidOperationException("RawMarketData.RollOverAtPercent must be between 1 and 99.");
    }
}
