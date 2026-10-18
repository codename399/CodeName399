using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Services;
using System.Text.Json;
using System.IO.Compression;

namespace CodeName399.Kite.API.Prediction;

/// <summary>Replays the raw archive without giving prediction code access to future ticks.</summary>
public sealed class PredictionReplayService
{
    private readonly PredictionOptions _options;
    private readonly ExpectedPredictionService _prediction;

    public PredictionReplayService(ExpectedPredictionService prediction, PredictionOptions? options = null)
    {
        _prediction = prediction;
        _options = options ?? prediction.Options;
    }

    public async Task<PredictionEvaluation> EvaluateZipAsync(
        string zipPath,
        double? trainFraction = null,
        CancellationToken cancellationToken = default) =>
        (await TrainAndEvaluateZipAsync(zipPath, trainFraction, cancellationToken)).Evaluation;

    public async Task<PredictionReplayResult> TrainAndEvaluateZipAsync(
        string zipPath,
        double? trainFraction = null,
        CancellationToken cancellationToken = default)
    {
        trainFraction ??= _options.TrainFraction;
        if (!File.Exists(zipPath)) throw new FileNotFoundException("Prediction replay ZIP was not found.", zipPath);
        if (trainFraction <= 0 || trainFraction >= 1) throw new ArgumentOutOfRangeException(nameof(trainFraction));

        var samples = new List<PredictionSample>();
        var states = new Dictionary<string, SymbolState>(StringComparer.Ordinal);
        var pending = new Dictionary<string, Queue<PendingPrediction>>(StringComparer.Ordinal);
        long rows = 0;

        await foreach (var tick in ReadTicksAsync(zipPath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            rows++;
            if (tick.Ltp <= 0 || string.IsNullOrWhiteSpace(tick.SymbolToken)) continue;

            if (!states.TryGetValue(tick.SymbolToken, out var state))
            {
                state = new SymbolState(_options.MaximumHistoryTicksPerSymbol);
                states[tick.SymbolToken] = state;
            }

            // Reject late exchange-time packets for the reconstructed state.
            if (state.LastExchangeTime != default && tick.ExchangeTime < state.LastExchangeTime)
                continue;

            state.Add(tick);
            state.LastExchangeTime = tick.ExchangeTime;

            if (!pending.TryGetValue(tick.SymbolToken, out var queue))
            {
                queue = new Queue<PendingPrediction>();
                pending[tick.SymbolToken] = queue;
            }

            foreach (var item in queue)
            {
                var windowEnd = item.Prediction.TimestampUtc.AddSeconds(_options.FutureWindowSeconds);
                if (tick.ExchangeTime <= item.Prediction.TimestampUtc ||
                    tick.ExchangeTime > windowEnd)
                    continue;

                if (tick.Ltp > item.MaximumFuturePrice)
                {
                    item.MaximumFuturePrice = tick.Ltp;
                    if (item.MaximumFuturePrice >= item.Prediction.TargetPrice &&
                        item.TargetReachedAtUtc == null)
                    {
                        item.TargetReachedAtUtc = tick.ExchangeTime;
                    }
                }
            }

            while (queue.Count > 0 &&
                   tick.ExchangeTime >= queue.Peek().Prediction.TimestampUtc.AddSeconds(_options.FutureWindowSeconds))
            {
                var item = queue.Dequeue();
                var reached = item.MaximumFuturePrice >= item.Prediction.TargetPrice;
                var sample = new PredictionSample(
                    item.Prediction,
                    reached,
                    reached ? item.TargetReachedAtUtc : null,
                    item.MaximumFuturePrice,
                    item.MaximumFuturePrice <= 0 || item.Prediction.EntryPrice <= 0
                        ? 0m
                        : (item.MaximumFuturePrice / item.Prediction.EntryPrice - 1m) * 100m);
                if (samples.Count < _options.MaximumSamples)
                    samples.Add(sample);
            }

            if (state.History.Count >= _options.MinimumHistoryTicks &&
                (state.LastPredictionTime == default ||
                 tick.ExchangeTime - state.LastPredictionTime >= TimeSpan.FromSeconds(_options.PredictionSpacingSeconds)))
            {
                var prediction = _prediction.CreatePointInTimeSample(tick, state.History);
                queue.Enqueue(new PendingPrediction(prediction, tick.Ltp));
                state.LastPredictionTime = tick.ExchangeTime;
            }

        }

        // Do not label the final pending predictions: their complete future window
        // was not present in the archive, so their outcome is unknown.
        pending.Clear();

        if (samples.Count < 10)
            throw new InvalidOperationException($"Only {samples.Count} prediction samples were produced from {rows} raw rows.");

        samples.Sort((a, b) => a.Prediction.TimestampUtc.CompareTo(b.Prediction.TimestampUtc));

        // The boundary is defined by prediction timestamp. A training label must also
        // be fully known before that boundary; otherwise the last training labels would
        // consume future observations from the evaluation period.
        var boundaryIndex = Math.Clamp(
            (int)Math.Floor(samples.Count * trainFraction.Value),
            1,
            samples.Count - 1);

        var evaluationStartUtc = samples[boundaryIndex].Prediction.TimestampUtc;
        var trainingSamples = samples
            .Where(x => x.Prediction.TimestampUtc.AddSeconds(_options.FutureWindowSeconds) <= evaluationStartUtc)
            .ToList();

        var evaluationSamples = samples
            .Where(x => x.Prediction.TimestampUtc >= evaluationStartUtc)
            .ToList();

        if (trainingSamples.Count < 10)
        {
            throw new InvalidOperationException(
                $"Only {trainingSamples.Count} training samples remain after the {_options.FutureWindowSeconds}-second label-horizon embargo.");
        }

        if (evaluationSamples.Count < 1)
            throw new InvalidOperationException("No out-of-sample prediction samples remain after the training boundary.");

        _prediction.ResetForTraining();
        foreach (var sample in trainingSamples)
            _prediction.Update(sample.Prediction, sample.TargetReached);

        _prediction.SaveModel();

        var test = evaluationSamples.Select(sample => sample with
        {
            Prediction = sample.Prediction with
            { Probability = _prediction.Score(sample.Prediction.Features) }
        }).ToList();

        var evaluation = CalculateEvaluation(test);

        return new PredictionReplayResult(
            evaluation,
            trainingSamples[0].Prediction.TimestampUtc,
            trainingSamples[^1].Prediction.TimestampUtc,
            evaluationSamples[0].Prediction.TimestampUtc,
            evaluationSamples[^1].Prediction.TimestampUtc,
            trainingSamples.Count,
            evaluationSamples.Count);
    }

    private static PredictionEvaluation CalculateEvaluation(IReadOnlyList<PredictionSample> samples)
    {
        var correct = 0;
        var brier = 0d;
        var logLoss = 0d;
        var probability = 0d;
        var positives = 0;

        foreach (var sample in samples)
        {
            var p = Math.Clamp(sample.Prediction.Probability, 1e-6, 1d - 1e-6);
            var y = sample.TargetReached ? 1d : 0d;
            if ((p >= 0.5) == sample.TargetReached) correct++;
            if (sample.TargetReached) positives++;
            brier += Math.Pow(p - y, 2);
            logLoss += -(y * Math.Log(p) + (1d - y) * Math.Log(1d - p));
            probability += p;
        }

        var actualRate = (double)positives / samples.Count;
        var baselineBrier = actualRate * (1d - actualRate);
        var buckets = BuildProbabilityBuckets(samples, actualRate);
        var ece = buckets.Where(x => x.SampleCount > 0)
            .Sum(x => (double)x.SampleCount / samples.Count *
                      Math.Abs(x.AveragePredictedProbability - x.ActualPositiveRate));
        var thresholds = BuildThresholdMetrics(samples);

        var brierScore = brier / samples.Count;
        var brierSkill = baselineBrier <= 0d ? 0d : 1d - (brierScore / baselineBrier);

        return new PredictionEvaluation(
            samples.Count,
            positives,
            (double)correct / samples.Count,
            brierScore,
            baselineBrier,
            brierSkill,
            probability / samples.Count,
            actualRate,
            logLoss / samples.Count,
            CalculateRocAuc(samples),
            CalculatePrAuc(samples),
            ece,
            buckets,
            thresholds);
    }

    private static IReadOnlyList<PredictionThresholdEvaluation> BuildThresholdMetrics(
        IReadOnlyList<PredictionSample> samples)
    {
        var thresholds = new[] { 0.10, 0.15, 0.20, 0.25, 0.30, 0.40, 0.50 };
        return thresholds.Select(threshold =>
        {
            var selected = samples.Where(x => x.Prediction.Probability >= threshold).ToArray();
            var selectedCount = selected.Length;
            var hits = selected.Count(x => x.TargetReached);
            var precision = selectedCount == 0 ? 0d : (double)hits / selectedCount;
            var recall = samples.Count(x => x.TargetReached) == 0
                ? 0d
                : (double)hits / samples.Count(x => x.TargetReached);
            var f1 = precision + recall == 0d ? 0d : 2d * precision * recall / (precision + recall);

            return new PredictionThresholdEvaluation(
                threshold,
                selectedCount,
                hits,
                precision,
                recall,
                f1,
                selectedCount == 0 ? 0d : (double)hits / selectedCount,
                selectedCount == 0 ? 0d : selected.Average(x => Math.Clamp(x.Prediction.Probability, 0d, 1d)),
                selectedCount == 0 ? 0d : selected.Average(x => (double)x.MaximumFavorableReturnPercent));
        }).ToArray();
    }

    private static double CalculateRocAuc(IReadOnlyList<PredictionSample> samples)
    {
        var positives = samples.Count(x => x.TargetReached);
        var negatives = samples.Count - positives;
        if (positives == 0 || negatives == 0) return 0.5d;

        // Mann-Whitney formulation: probability that a random positive receives
        // a higher score than a random negative, with ties worth half a win.
        var ranked = samples
            .Select(x => (Probability: Math.Clamp(x.Prediction.Probability, 0d, 1d), Positive: x.TargetReached))
            .OrderBy(x => x.Probability)
            .ToArray();

        double positiveRankSum = 0d;
        var i = 0;
        while (i < ranked.Length)
        {
            var j = i + 1;
            while (j < ranked.Length && ranked[j].Probability == ranked[i].Probability) j++;
            var averageRank = (i + 1 + j) / 2d;
            for (var k = i; k < j; k++)
                if (ranked[k].Positive) positiveRankSum += averageRank;
            i = j;
        }

        return (positiveRankSum - positives * (positives + 1) / 2d) / (positives * (double)negatives);
    }

    private static double CalculatePrAuc(IReadOnlyList<PredictionSample> samples)
    {
        var positives = samples.Count(x => x.TargetReached);
        if (positives == 0) return 0d;

        var ranked = samples
            .Select(x => (Probability: Math.Clamp(x.Prediction.Probability, 0d, 1d), Positive: x.TargetReached))
            .OrderByDescending(x => x.Probability)
            .ToArray();

        var area = 0d;
        var previousRecall = 0d;
        var previousPrecision = 1d;
        var tp = 0;

        for (var index = 0; index < ranked.Length; index++)
        {
            var item = ranked[index];
            if (item.Positive) tp++;
            var recall = (double)tp / positives;
            var predictedPositive = index + 1;
            var precision = (double)tp / predictedPositive;
            area += (recall - previousRecall) * (precision + previousPrecision) / 2d;
            previousRecall = recall;
            previousPrecision = precision;
        }

        return Math.Clamp(area, 0d, 1d);
    }

    private static IReadOnlyList<PredictionProbabilityBucket> BuildProbabilityBuckets(
        IReadOnlyList<PredictionSample> samples,
        double overallRate)
    {
        var ranges = new (string Label, double Lower, double Upper)[]
        {
            ("0-5%", 0.00, 0.05),
            ("5-10%", 0.05, 0.10),
            ("10-15%", 0.10, 0.15),
            ("15-20%", 0.15, 0.20),
            ("20-30%", 0.20, 0.30),
            ("30-40%", 0.30, 0.40),
            ("40-50%", 0.40, 0.50),
            ("50%+", 0.50, 1.01)
        };

        return ranges.Select(range =>
        {
            var bucket = samples.Where(x =>
            {
                var p = Math.Clamp(x.Prediction.Probability, 0d, 1d);
                return p >= range.Lower && p < range.Upper;
            }).ToArray();

            var count = bucket.Length;
            var positiveCount = bucket.Count(x => x.TargetReached);
            var actualRate = count == 0 ? 0d : (double)positiveCount / count;
            var averageProbability = count == 0 ? 0d : bucket.Average(x => Math.Clamp(x.Prediction.Probability, 0d, 1d));

            return new PredictionProbabilityBucket(
                range.Label,
                range.Lower,
                range.Upper,
                count,
                positiveCount,
                actualRate,
                averageProbability,
                overallRate <= 0d ? 0d : actualRate / overallRate);
        }).ToArray();
    }

    private static async IAsyncEnumerable<MarketTick> ReadTicksAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var outer = ZipFile.OpenRead(path);
        var direct = outer.GetEntry("market-ticks.jsonl");
        if (direct != null)
        {
            await foreach (var tick in ReadJsonLinesAsync(direct.Open(), cancellationToken)) yield return tick;
            yield break;
        }

        var nested = outer.Entries.FirstOrDefault(x => x.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));
        if (nested == null) throw new InvalidDataException("ZIP does not contain market-ticks.jsonl or a nested ZIP archive.");

        var temp = Path.Combine(Path.GetTempPath(), "codename399-prediction-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            await using (var source = nested.Open())
            await using (var target = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, true))
                await source.CopyToAsync(target, cancellationToken);

            using var inner = ZipFile.OpenRead(temp);
            var entry = inner.GetEntry("market-ticks.jsonl") ?? throw new InvalidDataException("Nested ZIP does not contain market-ticks.jsonl.");
            await foreach (var tick in ReadJsonLinesAsync(entry.Open(), cancellationToken)) yield return tick;
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static async IAsyncEnumerable<MarketTick> ReadJsonLinesAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var record = JsonSerializer.Deserialize<RawMarketTickRecord>(line);
            if (record == null) continue;
            var normalizedTick = new MarketTick
            {
                TradingSymbol = record.TradingSymbol,
                SymbolToken = record.SymbolToken,
                ExchangeType = record.ExchangeType,
                MarketDataMode = record.MarketDataMode,
                SequenceNumber = record.SequenceNumber,
                ExchangeTime = record.ExchangeTimeUtc,
                LastUpdated = record.ReceivedTimeUtc,
                Ltp = record.Ltp,
                Bid = record.Bid,
                Ask = record.Ask,
                Open = record.Open,
                High = record.High,
                Low = record.Low,
                Close = record.Close,
                PreviousClose = record.PreviousClose,
                LastTradedQuantity = record.LastTradedQuantity,
                AveragePrice = record.AveragePrice,
                DayVolume = record.DayVolume,
                Volume = record.Volume,
                TotalBuyQuantity = record.TotalBuyQuantity,
                TotalSellQuantity = record.TotalSellQuantity,
                BidDepth = record.BidDepth.ToList(),
                AskDepth = record.AskDepth.ToList()
            };
            PredictionMarketDataNormalizer.NormalizeLegacyArchiveQuote(normalizedTick);
            yield return normalizedTick;
        }
    }

    private sealed class SymbolState
    {
        private readonly int _capacity;
        public List<MarketTick> History { get; } = [];
        public DateTime LastExchangeTime { get; set; }
        public DateTime LastPredictionTime { get; set; }
        public SymbolState(int capacity) => _capacity = capacity;
        public void Add(MarketTick tick)
        {
            History.Add(tick);
            if (History.Count > _capacity) History.RemoveAt(0);
        }
    }

    private sealed class PendingPrediction(PredictionResult prediction, decimal price)
    {
        public PredictionResult Prediction { get; } = prediction;
        public decimal MaximumFuturePrice { get; set; } = price;
        public DateTime? TargetReachedAtUtc { get; set; }
    }
}
