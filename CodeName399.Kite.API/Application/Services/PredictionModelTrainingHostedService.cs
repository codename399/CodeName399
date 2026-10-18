using CodeName399.Kite.API.Prediction;
using CodeName399.Kite.API.Services;

namespace CodeName399.Kite.API.Services;

/// <summary>
/// Keeps the persisted prediction model ready without ever allowing a BUY to
/// bypass prediction. Training is background work so application startup and
/// market-data connectivity are not blocked.
/// </summary>
public sealed class PredictionModelTrainingHostedService : BackgroundService
{
    private readonly TradingSettingsProvider _settings;
    private readonly PredictionModelTrainingService _training;
    private readonly ILogger<PredictionModelTrainingHostedService> _logger;

    public PredictionModelTrainingHostedService(
        TradingSettingsProvider settings,
        PredictionModelTrainingService training,
        ILogger<PredictionModelTrainingHostedService> logger)
    {
        _settings = settings;
        _training = training;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuration = _settings.Current.Prediction;
        var startupDelay = Math.Clamp(configuration?.AutoTrainStartupDelaySeconds ?? 10, 0, 3600);

        if (startupDelay > 0)
            await Task.Delay(TimeSpan.FromSeconds(startupDelay), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _training.EnsureModelReadyAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected prediction model lifecycle error.");
            }

            if (_training.Status == "Ready" || !_settings.Current.Prediction.AutoTrainModel)
            {
                // Once ready, do not repeatedly retrain the live model. This protects
                // the active model from a bad/partial new archive. A deliberate retrain
                // can still be initiated through the existing training endpoint.
                await Task.Delay(TimeSpan.FromMinutes(
                    Math.Max(1, _settings.Current.Prediction.AutoTrainCheckIntervalMinutes)), stoppingToken);
            }
            else
            {
                await Task.Delay(TimeSpan.FromMinutes(
                    Math.Max(1, _settings.Current.Prediction.AutoTrainCheckIntervalMinutes)), stoppingToken);
            }
        }
    }
}
