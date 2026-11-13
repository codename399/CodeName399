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

        if (configuration.MinimumStockPrice < 0m)
            throw new InvalidOperationException("MinimumStockPrice cannot be negative.");
    }
}
