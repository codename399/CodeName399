using CodeName399.Kite.API.Prediction;
using CodeName399.Kite.API.Models.Configuration;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeName399.Kite.API.Controllers;

[Authorize]
[ApiController]
public sealed class PredictionController : ControllerBase
{
    private readonly ExpectedPredictionService _prediction;
    private readonly PredictionReplayService _replay;
    private readonly TradingSettingsProvider _settings;
    private readonly IHostEnvironment _environment;
    private readonly PredictionModelTrainingService _training;

    public PredictionController(
        ExpectedPredictionService prediction,
        PredictionReplayService replay,
        TradingSettingsProvider settings,
        IHostEnvironment environment,
        PredictionModelTrainingService training)
    {
        _prediction = prediction;
        _replay = replay;
        _settings = settings;
        _environment = environment;
        _training = training;
    }

    [HttpGet("trading/prediction/status")]
    public IActionResult Status()
    {
        return Ok(new
        {
            enabled = _prediction.IsEnabled,
            modelReady = _prediction.IsModelReady,
            modelVersion = _prediction.ModelVersion,
            modelFilePath = _prediction.ModelFilePath,
            trainedAtUtc = _prediction.TrainedAtUtc,
            training = new
            {
                status = _training.Status,
                inProgress = _training.InProgress,
                lastArchive = _training.LastArchive,
                lastError = _training.LastError,
                lastAttemptUtc = _training.LastAttemptUtc,
                lastSuccessUtc = _training.LastSuccessUtc,
                lastTrainingSamples = _training.LastTrainingSamples
            },
            configuration = _settings.Current.Prediction
        });
    }

    [HttpPost("trading/prediction/train")]
    public async Task<IActionResult> Train([FromBody] PredictionTrainingRequest? request, CancellationToken cancellationToken)
    {
        var configuration = _settings.Current.Prediction ?? new PredictionTradingSettings();
        var path = string.IsNullOrWhiteSpace(request?.ZipPath)
            ? configuration.TrainingDataPath
            : request.ZipPath;

        if (!Path.IsPathRooted(path))
            path = Path.Combine(_environment.ContentRootPath, path);

        if (Directory.Exists(path))
        {
            var zip = Directory.EnumerateFiles(path, "*.zip", SearchOption.TopDirectoryOnly)
                .OrderByDescending(System.IO.File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (zip == null)
                return BadRequest(new { success = false, message = $"No .zip training archive found in {path}." });
            path = zip;
        }

        if (!System.IO.File.Exists(path))
            return NotFound(new { success = false, message = $"Prediction training archive was not found: {path}" });

        var result = await _replay.TrainAndEvaluateZipAsync(
            path,
            request?.TrainFraction,
            cancellationToken);

        return Ok(new
        {
            success = true,
            modelReady = _prediction.IsModelReady,
            modelVersion = _prediction.ModelVersion,
            modelFilePath = _prediction.ModelFilePath,
            result
        });
    }
}

public sealed record PredictionTrainingRequest(string? ZipPath = null, double? TrainFraction = null);
