using CodeName399.Kite.API.Models.Configuration;
using Microsoft.Extensions.Logging;

namespace CodeName399.Kite.API.Prediction;

/// <summary>
/// Owns the lifecycle of the persisted equity prediction model.
/// It never bypasses prediction during trading: if a model cannot be built,
/// ExpectedPredictionService remains not-ready and BUY stays blocked.
/// </summary>
public sealed class PredictionModelTrainingService
{
    private readonly TradingSettingsProvider _settings;
    private readonly ExpectedPredictionService _prediction;
    private readonly PredictionReplayService _replay;
    private readonly ILogger<PredictionModelTrainingService> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private string _status = "NotStarted";
    private string? _lastArchive;
    private string? _lastError;
    private DateTime? _lastAttemptUtc;
    private DateTime? _lastSuccessUtc;
    private int _lastTrainingSamples;

    public PredictionModelTrainingService(
        TradingSettingsProvider settings,
        ExpectedPredictionService prediction,
        PredictionReplayService replay,
        ILogger<PredictionModelTrainingService> logger)
    {
        _settings = settings;
        _prediction = prediction;
        _replay = replay;
        _logger = logger;
    }

    public string Status => _status;
    public bool InProgress => _status == "Training";
    public string? LastArchive => _lastArchive;
    public string? LastError => _lastError;
    public DateTime? LastAttemptUtc => _lastAttemptUtc;
    public DateTime? LastSuccessUtc => _lastSuccessUtc;
    public int LastTrainingSamples => _lastTrainingSamples;

    public async Task<bool> EnsureModelReadyAsync(CancellationToken cancellationToken)
    {
        var configuration = _settings.Current.Prediction ?? new PredictionTradingSettings();

        if (_prediction.IsModelReady)
        {
            SetStatus("Ready");
            return true;
        }

        if (!configuration.AutoTrainModel)
        {
            SetStatus("WaitingForManualTraining");
            return false;
        }

        if (!await _gate.WaitAsync(0, cancellationToken))
            return false;

        try
        {
            // Re-check after taking the gate because another request may have trained it.
            if (_prediction.IsModelReady)
            {
                SetStatus("Ready");
                return true;
            }

            SetStatus("SearchingForTrainingData");
            _lastAttemptUtc = DateTime.UtcNow;
            _lastError = null;

            var archives = FindTrainingArchives(configuration);
            if (archives.Count == 0)
            {
                var path = ResolveTrainingPath(configuration.TrainingDataPath);
                _lastError = $"No usable prediction training ZIP found under '{path}'.";
                SetStatus("WaitingForTrainingData");
                _logger.LogWarning("Prediction model is not ready. {Reason}", _lastError);
                return false;
            }

            // Try newest first, then older archives. A partially written/new archive
            // may not contain enough complete prediction windows yet.
            foreach (var archive in archives)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _lastArchive = archive;
                SetStatus("Training");
                _logger.LogInformation(
                    "Prediction model training started. Archive={Archive} SizeBytes={SizeBytes} LastWriteUtc={LastWriteUtc}",
                    archive,
                    new FileInfo(archive).Length,
                    File.GetLastWriteTimeUtc(archive));

                try
                {
                    var result = await _replay.TrainAndEvaluateZipAsync(
                        archive,
                        configuration.TrainFraction,
                        cancellationToken);

                    _lastTrainingSamples = result.TrainingSampleCount;
                    _lastSuccessUtc = DateTime.UtcNow;
                    _lastError = null;
                    SetStatus("Ready");

                    _logger.LogInformation(
                        "Prediction model training completed. ModelReady={ModelReady} ModelPath={ModelPath} TrainingSamples={TrainingSamples} EvaluationSamples={EvaluationSamples} TrainedAtUtc={TrainedAtUtc}",
                        _prediction.IsModelReady,
                        _prediction.ModelFilePath,
                        result.TrainingSampleCount,
                        result.EvaluationSampleCount,
                        _prediction.TrainedAtUtc);

                    return _prediction.IsModelReady;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    SetStatus("Cancelled");
                    throw;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                    _logger.LogWarning(
                        ex,
                        "Prediction model training failed for archive {Archive}. Trying an older archive if available.",
                        archive);
                }
            }

            SetStatus("Failed");
            _logger.LogError(
                "Prediction model remains NOT READY after trying {ArchiveCount} training archives. LastError={LastError}",
                archives.Count,
                _lastError);
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private List<string> FindTrainingArchives(PredictionTradingSettings configuration)
    {
        var path = ResolveTrainingPath(configuration.TrainingDataPath);
        if (!Directory.Exists(path))
            return [];

        var minimumAge = TimeSpan.FromMinutes(Math.Max(0, configuration.AutoTrainMinimumArchiveAgeMinutes));
        var cutoff = DateTime.UtcNow - minimumAge;

        return Directory.EnumerateFiles(path, "market-ticks-*.zip", SearchOption.AllDirectories)
            .Where(File.Exists)
            .Where(file => File.GetLastWriteTimeUtc(file) <= cutoff)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToList();
    }

    private string ResolveTrainingPath(string configuredPath)
    {
        var environmentOverride = Environment.GetEnvironmentVariable("CODENAME399_RAW_MARKET_DATA_PATH");
        if (!string.IsNullOrWhiteSpace(environmentOverride))
            return environmentOverride;

        if (Path.IsPathRooted(configuredPath))
            return configuredPath;

        return Path.Combine(AppContext.BaseDirectory, configuredPath);
    }

    private void SetStatus(string status) => _status = status;
}
