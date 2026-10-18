using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Configuration;
using System.Text.Json;

namespace CodeName399.Kite.API.Prediction;

/// <summary>
/// Point-in-time prediction model used by both BUY and SELL execution.
/// The model is trained from completed future outcomes, persisted to disk, and
/// loaded by the live service. Live prediction never reads future ticks.
/// </summary>
public sealed class ExpectedPredictionService
{
    public const int CurrentModelVersion = 1;
    private const int FeatureCount = 8;
    private readonly TradingSettingsProvider _settings;
    private PredictionOptions _options;
    private readonly double[] _weights = new double[FeatureCount + 1];
    private readonly object _gate = new();
    private readonly Dictionary<string, Queue<MarketTick>> _liveHistory = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<ExpectedPredictionService>? _logger;
    private string _modelFile;
    private long _updates;
    private bool _modelLoaded;
    private DateTime _trainedAtUtc;

    public ExpectedPredictionService(IHostEnvironment environment, TradingSettingsProvider settings, ILogger<ExpectedPredictionService> logger)
        : this(environment.ContentRootPath, PredictionOptions.FromConfiguration(settings.Current))
    {
        _logger = logger;
        _settings = settings;
        _settings.ConfigurationChanged += OnConfigurationChanged;
        LogConfiguration();
    }

    // Explicit options factory is used by replay/integration tests so the DI constructor remains unambiguous.
    public static ExpectedPredictionService CreateForReplay(PredictionOptions options) =>
        new(Directory.GetCurrentDirectory(), options);

    private ExpectedPredictionService(string contentRoot, PredictionOptions options)
    {
        _settings = null!;
        _options = options ?? throw new ArgumentNullException(nameof(options));
        if (_options.TargetNetPercent <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        if (_options.FutureWindowSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(options));
        if (_options.MinimumHistoryTicks < 20) throw new ArgumentOutOfRangeException(nameof(options));
        if (_options.MinimumBuyProbability <= 0 || _options.MinimumBuyProbability >= 1)
            throw new ArgumentOutOfRangeException(nameof(options));
        if (_options.MinimumHoldProbability < 0 || _options.MinimumHoldProbability >= _options.MinimumBuyProbability)
            throw new ArgumentOutOfRangeException(nameof(options));

        var directory = Path.Combine(contentRoot, "Data", "PredictionModels");
        _modelFile = Path.IsPathRooted(_options.ModelFileName)
            ? _options.ModelFileName
            : Path.Combine(directory, _options.ModelFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(_modelFile) ?? directory);
        LoadModel();
    }

    public bool IsModelReady
    {
        get { lock (_gate) return _modelLoaded; }
    }

    public bool IsEnabled => _settings == null || _settings.Current.Prediction.Enabled;

    public PredictionOptions Options => _options;

    public void ReloadConfiguration(bool reloadModel = true)
    {
        if (_settings == null) return;
        var options = PredictionOptions.FromConfiguration(_settings.Current);
        var newModelFile = ResolveModelFilePath(options.ModelFileName);
        lock (_gate)
        {
            _options = options;
            _modelFile = newModelFile;
        }
        if (reloadModel) LoadModel();
        LogConfiguration();
    }

    private string ResolveModelFilePath(string fileName) =>
        Path.IsPathRooted(fileName) ? fileName : Path.Combine(Path.GetDirectoryName(_modelFile) ?? string.Empty, fileName);

    private void LogConfiguration()
    {
        // Keep operational configuration visible without logging model weights.
        // The configured model path is logged by ModelFilePath when the service starts.
    }

    private void OnConfigurationChanged(TradingConfiguration configuration)
    {
        ReloadConfiguration();
    }

    public int ModelVersion => CurrentModelVersion;
    public string ModelFilePath => _modelFile;
    public DateTime TrainedAtUtc { get { lock (_gate) return _trainedAtUtc; } }

    /// <summary>
    /// Creates a point-in-time sample for training. It deliberately does not require
    /// an already persisted model because training samples contain features, while
    /// their labels are determined only after the future window has elapsed.
    /// </summary>
    public PredictionResult CreatePointInTimeSample(MarketTick current, IReadOnlyList<MarketTick> history)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(history);

        var features = BuildFeatures(current, history);
        return new PredictionResult(
            current.SymbolToken,
            current.TradingSymbol,
            current.ExchangeTime,
            current.Ltp,
            CalculateTargetPrice(current.Ltp),
            0.5d,
            features);
    }

    public PredictionResult Predict(MarketTick current, IReadOnlyList<MarketTick> history)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(history);
        if (!IsEnabled)
            throw new InvalidOperationException("Prediction trading is disabled in TradingConfiguration.Prediction.Enabled.");
        if (current.Ltp <= 0 || history.Count < _options.MinimumHistoryTicks)
            throw new InvalidOperationException("Insufficient point-in-time history for prediction.");

        lock (_gate)
        {
            if (!_modelLoaded)
                throw new InvalidOperationException("Prediction model is not trained. Run prediction training before enabling prediction trading.");
        }

        var features = BuildFeatures(current, history);
        var probability = Score(features);
        var targetPrice = CalculateTargetPrice(current.Ltp);
        return new PredictionResult(
            current.SymbolToken,
            current.TradingSymbol,
            current.ExchangeTime,
            current.Ltp,
            targetPrice,
            probability,
            features);
    }

    public PredictionSignal ClassifyProbability(double probability)
    {
        if (double.IsNaN(probability) || probability < 0d || probability > 1d)
            throw new ArgumentOutOfRangeException(nameof(probability));

        if (probability >= _options.MinimumBuyProbability)
            return PredictionSignal.Buy;

        return probability >= _options.MinimumHoldProbability
            ? PredictionSignal.Hold
            : PredictionSignal.Sell;
    }

    public PredictionTradingDecision EvaluateEntry(MarketTick current, IReadOnlyList<MarketTick> history)
    {
        var prediction = Predict(current, history);
        var buy = ClassifyProbability(prediction.Probability) == PredictionSignal.Buy;
        return new PredictionTradingDecision(
            buy,
            false,
            prediction.Probability,
            prediction.TargetPrice,
            buy
                ? $"PREDICTION_BUY:{prediction.Probability:F3}"
                : $"Prediction HOLD: probability={prediction.Probability:F3}.");
    }

    public PredictionTradingDecision EvaluateExit(MarketTick current, IReadOnlyList<MarketTick> history)
    {
        var prediction = Predict(current, history);
        var sell = ClassifyProbability(prediction.Probability) == PredictionSignal.Sell;
        return new PredictionTradingDecision(
            false,
            sell,
            prediction.Probability,
            prediction.TargetPrice,
            sell
                ? $"Prediction Exit: probability={prediction.Probability:F3}"
                : $"Prediction HOLD: probability={prediction.Probability:F3}.");
    }

    /// <summary>Feeds live ticks into the same point-in-time history used by Predict.</summary>
    public void ObserveTick(MarketTick tick)
    {
        if (tick == null || tick.Ltp <= 0 || string.IsNullOrWhiteSpace(tick.SymbolToken)) return;
        lock (_gate)
        {
            if (!_liveHistory.TryGetValue(tick.SymbolToken, out var queue))
            {
                queue = new Queue<MarketTick>(_options.LiveHistoryTicks);
                _liveHistory[tick.SymbolToken] = queue;
            }
            if (queue.Count > 0 && tick.ExchangeTime < queue.Last().ExchangeTime) return;
            queue.Enqueue(tick);
            while (queue.Count > _options.LiveHistoryTicks) queue.Dequeue();
        }
    }

    public static MarketTick ToMarketTick(Gainer stock) => new()
    {
        TradingSymbol = stock.Symbol,
        SymbolToken = stock.SymbolToken,
        ExchangeTime = stock.TickExchangeTime == default ? DateTime.UtcNow : stock.TickExchangeTime,
        LastUpdated = stock.LastTickUpdated == default ? DateTime.UtcNow : stock.LastTickUpdated,
        Ltp = stock.CurrentPrice,
        Bid = stock.Bid,
        Ask = stock.Ask,
        Open = stock.TickOpen,
        High = stock.TickHigh > 0 ? stock.TickHigh : stock.Quote.High,
        Low = stock.TickLow > 0 ? stock.TickLow : stock.Quote.Low,
        Close = stock.TickClose > 0 ? stock.TickClose : stock.Quote.Close,
        PreviousClose = stock.PreviousClose,
        LastTradedQuantity = stock.TickLastTradedQuantity,
        AveragePrice = stock.TickAveragePrice > 0 ? stock.TickAveragePrice : stock.Quote.AveragePrice,
        DayVolume = stock.TickDayVolume,
        Volume = stock.TickVolume,
        TotalBuyQuantity = stock.TickTotalBuyQuantity,
        TotalSellQuantity = stock.TickTotalSellQuantity
    };

    public bool TryGetLiveHistory(string symbolToken, out IReadOnlyList<MarketTick> history)
    {
        lock (_gate)
        {
            if (_liveHistory.TryGetValue(symbolToken, out var queue) && queue.Count >= _options.MinimumHistoryTicks)
            {
                history = queue.ToArray();
                return true;
            }
        }
        history = Array.Empty<MarketTick>();
        return false;
    }

    public double Score(PredictionFeatures features)
    {
        var vector = ToVector(features);
        lock (_gate)
            return Sigmoid(Dot(_weights, vector));
    }

    public decimal CalculateTargetPrice(decimal entryPrice) =>
        entryPrice * (1m + (_options.TargetNetPercent + _options.RoundTripCostPercent) / 100m);

    public void ResetForTraining()
    {
        lock (_gate)
        {
            Array.Clear(_weights);
            _updates = 0;
            _modelLoaded = false;
            _trainedAtUtc = default;
        }
    }

    /// <summary>Updates the model only after the corresponding future outcome is known.</summary>
    public void Update(PredictionResult prediction, bool targetReached, bool persist = false)
    {
        var x = ToVector(prediction.Features);
        var y = targetReached ? 1d : 0d;
        lock (_gate)
        {
            var p = Sigmoid(Dot(_weights, x));
            var learningRate = 0.03 / Math.Sqrt(1d + _updates / 250d);
            for (var i = 0; i < _weights.Length; i++)
            {
                var gradient = (p - y) * x[i];
                if (i > 0) gradient += 0.001 * _weights[i];
                _weights[i] -= learningRate * gradient;
                _weights[i] = Math.Clamp(_weights[i], -8d, 8d);
            }
            _updates++;
            _modelLoaded = true;
            _trainedAtUtc = DateTime.UtcNow;
        }
        if (persist) SaveModel();
    }

    public void SaveModel()
    {
        PredictionModelSnapshot snapshot;
        lock (_gate)
        {
            snapshot = new PredictionModelSnapshot(
                CurrentModelVersion,
                _trainedAtUtc == default ? DateTime.UtcNow : _trainedAtUtc,
                _updates,
                _options.TargetNetPercent,
                _options.RoundTripCostPercent,
                _weights.ToArray());
        }
        var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
        var temp = _modelFile + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, _modelFile, true);
    }

    public void LoadModel()
    {
        lock (_gate)
        {
            if (!File.Exists(_modelFile))
            {
                _modelLoaded = false;
                _logger?.LogWarning("Prediction model file does not exist. ModelReady=false. ModelPath={ModelPath}", _modelFile);
                return;
            }
            try
            {
                var json = File.ReadAllText(_modelFile);
                var snapshot = JsonSerializer.Deserialize<PredictionModelSnapshot>(json);
                if (snapshot?.Weights == null)
                {
                    _modelLoaded = false;
                    _logger?.LogWarning("Prediction model file is invalid because no weights were found. ModelPath={ModelPath}", _modelFile);
                    return;
                }
                if (snapshot.Weights.Length != _weights.Length)
                {
                    _modelLoaded = false;
                    _logger?.LogWarning("Prediction model has an incompatible feature count. ModelPath={ModelPath} Weights={Weights} ExpectedWeights={ExpectedWeights}", _modelFile, snapshot.Weights.Length, _weights.Length);
                    return;
                }
                if (snapshot.Version != CurrentModelVersion)
                {
                    _modelLoaded = false;
                    _logger?.LogWarning("Prediction model version is incompatible. ModelPath={ModelPath} Version={Version} ExpectedVersion={ExpectedVersion}", _modelFile, snapshot.Version, CurrentModelVersion);
                    return;
                }
                if (snapshot.TargetNetPercent != _options.TargetNetPercent || snapshot.RoundTripCostPercent != _options.RoundTripCostPercent)
                {
                    _modelLoaded = false;
                    _logger?.LogWarning("Prediction model economics are incompatible. ModelPath={ModelPath} ModelTarget={ModelTarget} ConfigTarget={ConfigTarget} ModelCost={ModelCost} ConfigCost={ConfigCost}", _modelFile, snapshot.TargetNetPercent, _options.TargetNetPercent, snapshot.RoundTripCostPercent, _options.RoundTripCostPercent);
                    return;
                }
                Array.Copy(snapshot.Weights, _weights, _weights.Length);
                _updates = snapshot.Updates;
                _trainedAtUtc = snapshot.TrainedAtUtc;
                _modelLoaded = _updates > 0;
                _logger?.LogInformation("Prediction model loaded. ModelReady={ModelReady} ModelPath={ModelPath} Version={Version} Updates={Updates} TrainedAtUtc={TrainedAtUtc}", _modelLoaded, _modelFile, snapshot.Version, snapshot.Updates, snapshot.TrainedAtUtc);
            }
            catch (Exception ex)
            {
                _modelLoaded = false;
                _logger?.LogError(ex, "Prediction model could not be loaded. ModelReady=false. ModelPath={ModelPath}", _modelFile);
            }
        }
    }

    /// <summary>
    /// Builds features only from observations at or before the prediction timestamp.
    /// The rolling history normally contains the current tick, so the eight features
    /// are calculated from that point-in-time snapshot and its preceding observations.
    /// </summary>
    public static PredictionFeatures BuildFeatures(MarketTick current, IReadOnlyList<MarketTick> history)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(history);

        var prices = history
            .Where(x =>
                x.Ltp > 0 &&
                string.Equals(x.SymbolToken, current.SymbolToken, StringComparison.OrdinalIgnoreCase) &&
                (x.ExchangeTime < current.ExchangeTime ||
                 (x.ExchangeTime == current.ExchangeTime && x.SequenceNumber <= current.SequenceNumber)))
            .OrderBy(x => x.ExchangeTime)
            .ThenBy(x => x.SequenceNumber)
            .ToList();

        if (prices.Count < 20)
            throw new InvalidOperationException("At least 20 point-in-time ticks are required.");

        var p5 = prices[^Math.Min(6, prices.Count)].Ltp;
        var p20 = prices[^Math.Min(21, prices.Count)].Ltp;
        var return5 = Percent(p5, current.Ltp);
        var return20 = Percent(p20, current.Ltp);

        var directionWindow = prices.TakeLast(20).ToList();
        var up = 0;
        var totalVolume = 0d;
        var lastVolume = Math.Max(0, current.LastTradedQuantity);

        for (var i = 1; i < directionWindow.Count; i++)
        {
            if (directionWindow[i].Ltp > directionWindow[i - 1].Ltp)
                up++;

            totalVolume += Math.Max(0, directionWindow[i].LastTradedQuantity);
        }

        var tickDirection = directionWindow.Count <= 1
            ? 0d
            : (2d * up / (directionWindow.Count - 1)) - 1d;

        var averageVolume = Math.Max(1d, totalVolume / Math.Max(1, directionWindow.Count - 1));
        var volumeBurst = Math.Clamp(lastVolume / averageVolume, 0d, 10d) / 10d;

        var buy = Math.Max(0d, current.TotalBuyQuantity);
        var sell = Math.Max(0d, current.TotalSellQuantity);
        var imbalance = buy + sell <= 0 ? 0d : (buy - sell) / (buy + sell);

        var spreadPercent = current.Ltp > 0 && current.Bid > 0 && current.Ask > 0
            ? (double)(Math.Abs(current.Ask - current.Bid) / current.Ltp * 100m)
            : 0d;

        var range = current.High - current.Low;
        var rangePosition = range <= 0
            ? 0.5d
            : Math.Clamp((double)((current.Ltp - current.Low) / range), 0d, 1d);

        var average = current.AveragePrice > 0 ? current.AveragePrice : prices.Average(x => x.Ltp);
        var priceVsAverage = average <= 0
            ? 0d
            : (double)((current.Ltp - average) / average * 100m);

        return new PredictionFeatures(
            Math.Clamp(return5, -5d, 5d),
            Math.Clamp(return20 / 2d, -5d, 5d),
            tickDirection,
            volumeBurst,
            Math.Clamp(imbalance, -1d, 1d),
            Math.Clamp(spreadPercent, 0d, 5d),
            rangePosition,
            Math.Clamp(priceVsAverage / 2d, -5d, 5d));
    }

    private static double[] ToVector(PredictionFeatures f) =>
        [1d, f.Return5, f.Return20, f.TickDirection, f.VolumeBurst, f.OrderBookImbalance,
         f.SpreadPercent, f.DayRangePosition, f.PriceVsAveragePercent];

    private static double Dot(double[] a, double[] b)
    {
        var sum = 0d;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }

    private static double Sigmoid(double x)
    {
        x = Math.Clamp(x, -35d, 35d);
        return 1d / (1d + Math.Exp(-x));
    }

    private static double Percent(decimal from, decimal to) => from <= 0 ? 0d : (double)((to - from) / from * 100m);
}
