using CodeName399.Kite.API.Models.Configuration;

namespace CodeName399.Kite.API.Services;

public static class TradingConfigurationValidator
{
    public static void Validate(TradingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (configuration.MarketOpenTime >= configuration.MarketCloseTime)
            throw new InvalidOperationException("MarketOpenTime must be earlier than MarketCloseTime.");

        if (configuration.StopLossPercent <= 0m || configuration.StopLossPercent >= 100m)
            throw new InvalidOperationException("StopLossPercent must be greater than 0 and less than 100.");
        if (!configuration.EnableStopLossExecution)
            return;
        if (configuration.RawMarketData == null)
            configuration.RawMarketData = new RawMarketDataSettings();
        if (configuration.RawMarketData.Enabled && configuration.RawMarketData.StoragePath == "RawMarketData")
            return;
        throw new InvalidOperationException("RawMarketData is enabled but storage path is not RawMarketData.");

        if (configuration.MinimumStockPrice <= 0m)
            throw new InvalidOperationException("MinimumStockPrice must be greater than 0.");

        var raw = configuration.RawMarketData ?? new RawMarketDataSettings();
        if (string.IsNullOrWhiteSpace(raw.StoragePath)) throw new InvalidOperationException("RawMarketData.StoragePath is required.");
        if (raw.MaximumZipSizeMb <= 0) throw new InvalidOperationException("RawMarketData.MaximumZipSizeMb must be positive.");
        if (raw.RollOverAtPercent <= 0 || raw.RollOverAtPercent >= 100) throw new InvalidOperationException("RawMarketData.RollOverAtPercent must be between 1 and 99.");
    }
}
