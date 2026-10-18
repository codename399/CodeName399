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

        var p = configuration.Prediction ?? new Models.Configuration.PredictionTradingSettings();
        if (p.TargetNetPercent <= 0) throw new InvalidOperationException("Prediction.TargetNetPercent must be positive.");
        if (p.RoundTripCostPercent < 0) throw new InvalidOperationException("Prediction.RoundTripCostPercent cannot be negative.");
        if (p.FutureWindowSeconds <= 0) throw new InvalidOperationException("Prediction.FutureWindowSeconds must be positive.");
        if (p.MinimumHistoryTicks < 20) throw new InvalidOperationException("Prediction.MinimumHistoryTicks must be at least 20.");
        if (p.MaximumHistoryTicksPerSymbol < p.MinimumHistoryTicks) throw new InvalidOperationException("Prediction.MaximumHistoryTicksPerSymbol must be >= MinimumHistoryTicks.");
        if (p.LiveHistoryTicks < p.MinimumHistoryTicks) throw new InvalidOperationException("Prediction.LiveHistoryTicks must be >= MinimumHistoryTicks.");
        if (p.PredictionSpacingSeconds <= 0) throw new InvalidOperationException("Prediction.PredictionSpacingSeconds must be positive.");
        if (p.MaximumSamples <= 0) throw new InvalidOperationException("Prediction.MaximumSamples must be positive.");
        if (p.MinimumBuyProbability <= 0 || p.MinimumBuyProbability >= 1) throw new InvalidOperationException("Prediction.MinimumBuyProbability must be between 0 and 1.");
        if (p.MinimumHoldProbability < 0 || p.MinimumHoldProbability >= p.MinimumBuyProbability) throw new InvalidOperationException("Prediction.MinimumHoldProbability must be >= 0 and below MinimumBuyProbability.");
        if (p.TrainFraction <= 0 || p.TrainFraction >= 1) throw new InvalidOperationException("Prediction.TrainFraction must be between 0 and 1.");
        if (string.IsNullOrWhiteSpace(p.ModelFileName)) throw new InvalidOperationException("Prediction.ModelFileName is required.");
        if (p.SimulationStartingCapital <= 0) throw new InvalidOperationException("Prediction.SimulationStartingCapital must be positive.");
        if (p.SimulationMaximumRiskPerTrade <= 0) throw new InvalidOperationException("Prediction.SimulationMaximumRiskPerTrade must be positive.");
        if (p.SimulationMaxCapitalPerTradePercent <= 0 || p.SimulationMaxCapitalPerTradePercent > 100) throw new InvalidOperationException("Prediction.SimulationMaxCapitalPerTradePercent must be between 0 and 100.");
        if (p.SimulationProtectiveStopPercent <= 0 || p.SimulationProtectiveStopPercent >= 100) throw new InvalidOperationException("Prediction.SimulationProtectiveStopPercent must be between 0 and 100.");
        var raw = configuration.RawMarketData ?? new RawMarketDataSettings();
        if (string.IsNullOrWhiteSpace(raw.StoragePath)) throw new InvalidOperationException("RawMarketData.StoragePath is required.");
        if (raw.MaximumZipSizeMb <= 0) throw new InvalidOperationException("RawMarketData.MaximumZipSizeMb must be positive.");
        if (raw.RollOverAtPercent <= 0 || raw.RollOverAtPercent >= 100) throw new InvalidOperationException("RawMarketData.RollOverAtPercent must be between 1 and 99.");
    }
}
