using CodeName399.Kite.API.Models.Configuration;

namespace CodeName399.Kite.API.Prediction;

public sealed record PredictionOptions
{
    public decimal TargetNetPercent { get; init; } = 0.25m;
    public decimal RoundTripCostPercent { get; init; } = 0.12m;
    public int FutureWindowSeconds { get; init; } = 300;
    public int MinimumHistoryTicks { get; init; } = 20;
    public int PredictionSpacingSeconds { get; init; } = 5;
    public int MaximumHistoryTicksPerSymbol { get; init; } = 120;
    public int MaximumSamples { get; init; } = 1_000_000;
    public double MinimumBuyProbability { get; init; } = 0.25d;
    public double MinimumHoldProbability { get; init; } = 0.15d;
    public int LiveHistoryTicks { get; init; } = 120;
    public string ModelFileName { get; init; } = "equity-prediction-model.json";
    public double TrainFraction { get; init; } = 0.70d;

    public static PredictionOptions FromConfiguration(TradingConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var p = configuration.Prediction ?? new PredictionTradingSettings();
        return new PredictionOptions
        {
            TargetNetPercent = p.TargetNetPercent,
            RoundTripCostPercent = p.RoundTripCostPercent,
            FutureWindowSeconds = p.FutureWindowSeconds,
            MinimumHistoryTicks = p.MinimumHistoryTicks,
            PredictionSpacingSeconds = p.PredictionSpacingSeconds,
            MaximumHistoryTicksPerSymbol = p.MaximumHistoryTicksPerSymbol,
            MaximumSamples = p.MaximumSamples,
            MinimumBuyProbability = p.MinimumBuyProbability,
            MinimumHoldProbability = p.MinimumHoldProbability,
            LiveHistoryTicks = p.LiveHistoryTicks,
            ModelFileName = p.ModelFileName,
            TrainFraction = p.TrainFraction
        };
    }
}

public sealed record PredictionFeatures(
    double Return5,
    double Return20,
    double TickDirection,
    double VolumeBurst,
    double OrderBookImbalance,
    double SpreadPercent,
    double DayRangePosition,
    double PriceVsAveragePercent);



public enum PredictionSignal
{
    Buy,
    Hold,
    Sell
}

public sealed record PredictionTradingDecision(
    bool ShouldBuy,
    bool ShouldSell,
    double Probability,
    decimal TargetPrice,
    string Reason);

public sealed record PredictionModelSnapshot(
    int Version,
    DateTime TrainedAtUtc,
    long Updates,
    decimal TargetNetPercent,
    decimal RoundTripCostPercent,
    double[] Weights);

public sealed record PredictionResult(
    string SymbolToken,
    string TradingSymbol,
    DateTime TimestampUtc,
    decimal EntryPrice,
    decimal TargetPrice,
    double Probability,
    PredictionFeatures Features);

public sealed record PredictionSample(
    PredictionResult Prediction,
    bool TargetReached,
    DateTime? TargetReachedAtUtc,
    decimal MaximumFuturePrice,
    decimal MaximumFavorableReturnPercent);

public sealed record PredictionProbabilityBucket(
    string Label,
    double LowerInclusive,
    double UpperExclusive,
    int SampleCount,
    int PositiveCount,
    double ActualPositiveRate,
    double AveragePredictedProbability,
    double LiftVsOverallRate);

public sealed record PredictionThresholdEvaluation(
    double Threshold,
    int SelectedSamples,
    int PositiveCount,
    double Precision,
    double Recall,
    double F1,
    double ActualPositiveRate,
    double AveragePredictedProbability,
    double AverageMaximumFavorableReturnPercent);

public sealed record PredictionEvaluation(
    int SampleCount,
    int PositiveCount,
    double AccuracyAt50Percent,
    double BrierScore,
    double BaselineBrierScore,
    double BrierSkill,
    double AveragePredictedProbability,
    double ActualPositiveRate,
    double LogLoss,
    double RocAuc,
    double PrAuc,
    double ExpectedCalibrationError,
    IReadOnlyList<PredictionProbabilityBucket> ProbabilityBuckets,
    IReadOnlyList<PredictionThresholdEvaluation> Thresholds);


public sealed record PredictionReplayResult(
    PredictionEvaluation Evaluation,
    DateTime TrainingStartUtc,
    DateTime TrainingEndUtc,
    DateTime EvaluationStartUtc,
    DateTime EvaluationEndUtc,
    int TrainingSampleCount,
    int EvaluationSampleCount);
