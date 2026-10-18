using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Services;
using CodeName399.Kite.API.Services.Brokerage;
using System.IO.Compression;
using System.Text.Json;

namespace CodeName399.Kite.API.Prediction;

public sealed record PredictionTradingSimulationOptions
{
    public double BuyProbability { get; init; } = 0.25d;
    public double SellProbability { get; init; } = 0.15d;
    public decimal StartingCapital { get; init; } = 100_000m;
    public decimal MaximumRiskPerTrade { get; init; } = 10_000m;
    public decimal MaxCapitalPerTradePercent { get; init; } = 20m;
    public decimal ProtectiveStopPercent { get; init; } = 0.50m;
    public int TrainFractionPercent { get; init; } = 70;
    public int MinimumHistoryTicks { get; init; } = 20;
    public int MaximumHistoryTicksPerSymbol { get; init; } = 120;
}

public sealed record PredictionTradingSimulationTrade(
    string Symbol,
    string SymbolToken,
    DateTime EntryTimeUtc,
    decimal EntryPrice,
    decimal EntryBid,
    decimal EntryAsk,
    double EntryProbability,
    string EntryDecision,
    int Quantity,
    decimal ProtectiveStop,
    DateTime ExitTimeUtc,
    decimal ExitPrice,
    decimal ExitBid,
    decimal ExitAsk,
    double ExitProbability,
    string ExitCategory,
    string ExitReason,
    decimal GrossProfit,
    decimal Brokerage,
    decimal Charges,
    decimal NetProfit,
    TimeSpan HoldingDuration);

public sealed record PredictionTradingSimulationResult(
    int TotalPredictionEvaluations,
    int BuySignals,
    int HoldDecisions,
    int SellSignals,
    int CompletedTrades,
    int WinningTrades,
    int LosingTrades,
    double WinRate,
    decimal GrossProfit,
    decimal GrossLoss,
    decimal GrossPnl,
    decimal Brokerage,
    decimal OtherCharges,
    decimal TotalCharges,
    decimal NetPnl,
    decimal AverageWinningTrade,
    decimal AverageLosingTrade,
    decimal ProfitFactor,
    decimal MaximumDrawdown,
    int MaximumConsecutiveLosses,
    TimeSpan AverageHoldingTime,
    decimal StartingCapital,
    decimal EndingCapital,
    decimal PeakCapitalDeployed,
    decimal PeakCapitalUtilizationPercent,
    decimal TotalEntryTurnover,
    DateTime TrainingStartUtc,
    DateTime TrainingEndUtc,
    DateTime EvaluationStartUtc,
    DateTime EvaluationEndUtc,
    PredictionEvaluation PredictionEvaluation,
    IReadOnlyDictionary<string, decimal> NetPnlBySymbol,
    IReadOnlyDictionary<DateOnly, decimal> NetPnlByDate,
    IReadOnlyList<PredictionTradingSimulationTrade> TradeDetails);

/// <summary>
/// Replays the trained model on an unseen chronological period. Prediction probability
/// is the only strategy signal. Capital, risk, charges, protective stops and trailing
/// protection are execution safeguards.
/// </summary>
public sealed class PredictionTradingSimulationService
{
    private readonly PredictionReplayService _replay;
    private readonly ExpectedPredictionService _prediction;
    private readonly IBrokerageCalculatorService _brokerage;
    private readonly TradingSettingsProvider _settings;

    public PredictionTradingSimulationService(
        PredictionReplayService replay,
        ExpectedPredictionService prediction,
        IBrokerageCalculatorService brokerage,
        TradingSettingsProvider settings)
    {
        _replay = replay;
        _prediction = prediction;
        _brokerage = brokerage;
        _settings = settings;
    }

    public async Task<PredictionTradingSimulationResult> SimulateZipAsync(
        string zipPath,
        PredictionTradingSimulationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            var p = _settings.Current.Prediction;
            options = new PredictionTradingSimulationOptions
            {
                BuyProbability = p.MinimumBuyProbability,
                SellProbability = p.MinimumHoldProbability,
                StartingCapital = p.SimulationStartingCapital,
                MaximumRiskPerTrade = p.SimulationMaximumRiskPerTrade,
                MaxCapitalPerTradePercent = p.SimulationMaxCapitalPerTradePercent,
                ProtectiveStopPercent = p.SimulationProtectiveStopPercent,
                TrainFractionPercent = (int)Math.Round(p.TrainFraction * 100d),
                MinimumHistoryTicks = p.MinimumHistoryTicks,
                MaximumHistoryTicksPerSymbol = p.MaximumHistoryTicksPerSymbol
            };
        }

        if (!File.Exists(zipPath))
            throw new FileNotFoundException("Prediction simulation ZIP was not found.", zipPath);

        if (options.BuyProbability <= options.SellProbability)
            throw new ArgumentException("BuyProbability must be greater than SellProbability.");

        if (options.StartingCapital <= 0m)
            throw new ArgumentOutOfRangeException(nameof(options.StartingCapital));

        if (options.MaximumRiskPerTrade <= 0m)
            throw new ArgumentOutOfRangeException(nameof(options.MaximumRiskPerTrade));

        if (options.ProtectiveStopPercent <= 0m || options.ProtectiveStopPercent >= 100m)
            throw new ArgumentOutOfRangeException(nameof(options.ProtectiveStopPercent));

        if (options.MaxCapitalPerTradePercent <= 0m || options.MaxCapitalPerTradePercent > 100m)
            throw new ArgumentOutOfRangeException(nameof(options.MaxCapitalPerTradePercent));

        // Training and the evaluation boundary are determined from completed prediction
        // samples, not from an unrelated raw-tick index.
        var replay = await _replay.TrainAndEvaluateZipAsync(
            zipPath,
            options.TrainFractionPercent / 100d,
            cancellationToken);

        var ticks = new List<MarketTick>();
        await foreach (var tick in ReadTicksAsync(zipPath, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (tick.Ltp <= 0 || string.IsNullOrWhiteSpace(tick.SymbolToken))
                continue;

            ticks.Add(tick);
        }

        ticks.Sort((a, b) =>
        {
            var time = a.ExchangeTime.CompareTo(b.ExchangeTime);
            return time != 0 ? time : a.SequenceNumber.CompareTo(b.SequenceNumber);
        });

        var histories = new Dictionary<string, Queue<MarketTick>>(StringComparer.OrdinalIgnoreCase);
        var positions = new Dictionary<string, OpenPosition>(StringComparer.OrdinalIgnoreCase);
        var trades = new List<PredictionTradingSimulationTrade>();

        var cash = options.StartingCapital;
        var peakCapitalDeployed = 0m;
        var deployedCash = 0m;
        var totalEntryTurnover = 0m;
        var maximumDrawdown = 0m;
        var predictionEvaluations = 0;
        var buySignals = 0;
        var holdDecisions = 0;
        var sellSignals = 0;

        foreach (var tick in ticks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (tick.ExchangeTime < replay.EvaluationStartUtc)
                continue;

            if (!histories.TryGetValue(tick.SymbolToken, out var history))
            {
                history = new Queue<MarketTick>(options.MaximumHistoryTicksPerSymbol);
                histories[tick.SymbolToken] = history;
            }

            if (history.Count > 0 && tick.ExchangeTime < history.Last().ExchangeTime)
                continue;

            history.Enqueue(tick);
            while (history.Count > options.MaximumHistoryTicksPerSymbol)
                history.Dequeue();

            if (history.Count < options.MinimumHistoryTicks)
                continue;

            if (!_prediction.IsModelReady)
                throw new InvalidOperationException("Prediction model is not ready for out-of-sample simulation.");

            PredictionResult prediction;
            try
            {
                prediction = _prediction.Predict(tick, history.ToArray());
            }
            catch (InvalidOperationException)
            {
                continue;
            }

            predictionEvaluations++;

            var probability = prediction.Probability;
            var hasExecutableQuote = HasExecutableQuote(tick);
            var executableEntry = hasExecutableQuote ? tick.Ask : 0m;
            var executableExit = hasExecutableQuote ? tick.Bid : 0m;

            if (probability >= options.BuyProbability)
                buySignals++;
            else if (probability < options.SellProbability)
                sellSignals++;
            else
                holdDecisions++;

            if (positions.TryGetValue(tick.SymbolToken, out var openPosition))
            {
                // A crossed or incomplete quote cannot be used to manufacture an
                // executable exit. Keep the position open until a valid bid is seen.
                if (hasExecutableQuote)
                {
                    if (probability < options.SellProbability)
                    {
                        await ClosePositionAsync(
                            openPosition,
                            tick,
                            probability,
                            "STRATEGY_EXIT",
                            $"Prediction Exit: probability={probability:F3}",
                            executableExit,
                            trades,
                            cancellationToken);

                        cash += trades[^1].NetProfit + openPosition.EntryCashReserved;
                        deployedCash = Math.Max(0m, deployedCash - openPosition.EntryCashReserved);
                        positions.Remove(tick.SymbolToken);
                    }
                    else
                    {
                        UpdateTrailingStop(openPosition, tick, executableExit);
                        if (executableExit <= openPosition.StopLoss)
                        {
                            await ClosePositionAsync(
                                openPosition,
                                tick,
                                probability,
                                "SAFETY_EXIT",
                                "Protective/trailing stop",
                                executableExit,
                                trades,
                                cancellationToken);

                            cash += trades[^1].NetProfit + openPosition.EntryCashReserved;
                            deployedCash = Math.Max(0m, deployedCash - openPosition.EntryCashReserved);
                            positions.Remove(tick.SymbolToken);
                        }
                    }
                }

                continue;
            }

            if (probability >= options.BuyProbability)
            {
                // Crossed quotes are invalid market-state data, not an opportunity.
                if (!hasExecutableQuote)
                    continue;

                var capitalForTrade = Math.Min(
                    cash,
                    cash * options.MaxCapitalPerTradePercent / 100m);

                if (capitalForTrade <= 0m)
                    continue;

                var stop = Math.Round(
                    executableEntry * (1m - options.ProtectiveStopPercent / 100m),
                    2,
                    MidpointRounding.AwayFromZero);

                var quantity = await CalculateQuantityAsync(
                    tick,
                    executableEntry,
                    stop,
                    capitalForTrade,
                    options,
                    cancellationToken);

                if (quantity <= 0)
                    continue;

                var entryCharges = await _brokerage.CalculateAsync(
                    tick.TradingSymbol,
                    tick.SymbolToken,
                    executableEntry,
                    quantity,
                    BrokerageTransactionType.Buy,
                    "NSE",
                    "INTRADAY");

                var reservedCash = executableEntry * quantity + entryCharges.TotalCharges;
                if (reservedCash > cash)
                    continue;

                var newPosition = new OpenPosition(
                    tick.SymbolToken,
                    tick.TradingSymbol,
                    tick.ExchangeTime,
                    executableEntry,
                    tick.Bid,
                    tick.Ask,
                    quantity,
                    stop,
                    probability,
                    entryCharges.TotalCharges,
                    reservedCash,
                    options.ProtectiveStopPercent);

                positions[tick.SymbolToken] = newPosition;
                cash -= reservedCash;
                deployedCash += reservedCash;
                totalEntryTurnover += executableEntry * quantity;
                peakCapitalDeployed = Math.Max(peakCapitalDeployed, deployedCash);

                continue;
            }

        }

        // A final close is necessary to make the out-of-sample portfolio accounting
        // complete, but it is explicitly classified as a safety/data-end close rather
        // than a strategy SELL.
        foreach (var closingPosition in positions.Values.ToArray())
        {
            var last = ticks
                .Where(x => string.Equals(x.SymbolToken, closingPosition.SymbolToken, StringComparison.OrdinalIgnoreCase)
                            && x.ExchangeTime >= replay.EvaluationStartUtc)
                .LastOrDefault();

            if (last == null || !HasExecutableQuote(last))
                continue;

            await ClosePositionAsync(
                closingPosition,
                last,
                _prediction.IsModelReady ? _prediction.Predict(last, histories[closingPosition.SymbolToken].ToArray()).Probability : 0d,
                "SAFETY_EXIT",
                "END_OF_EVALUATION_DATA",
                last.Bid,
                trades,
                cancellationToken);

            cash += trades[^1].NetProfit + closingPosition.EntryCashReserved;
            deployedCash = Math.Max(0m, deployedCash - closingPosition.EntryCashReserved);
            positions.Remove(closingPosition.SymbolToken);
        }

        var grossProfit = trades.Where(x => x.GrossProfit > 0m).Sum(x => x.GrossProfit);
        var grossLoss = trades.Where(x => x.GrossProfit < 0m).Sum(x => Math.Abs(x.GrossProfit));
        var grossPnl = trades.Sum(x => x.GrossProfit);
        var brokerage = trades.Sum(x => x.Brokerage);
        var charges = trades.Sum(x => x.Charges);
        var netPnl = trades.Sum(x => x.NetProfit);
        var wins = trades.Count(x => x.NetProfit > 0m);
        var losses = trades.Count(x => x.NetProfit < 0m);
        var winRate = trades.Count == 0 ? 0d : (double)wins / trades.Count;
        var averageWin = wins == 0 ? 0m : trades.Where(x => x.NetProfit > 0m).Average(x => x.NetProfit);
        var averageLoss = losses == 0 ? 0m : trades.Where(x => x.NetProfit < 0m).Average(x => Math.Abs(x.NetProfit));
        var profitFactor = grossLoss == 0m
            ? (grossProfit > 0m ? decimal.MaxValue : 0m)
            : grossProfit / grossLoss;

        var equity = options.StartingCapital;
        var peak = equity;
        var maxLoss = 0m;
        var consecutiveLosses = 0;
        var maximumConsecutiveLosses = 0;

        foreach (var trade in trades.OrderBy(x => x.ExitTimeUtc))
        {
            equity += trade.NetProfit;
            if (equity > peak)
                peak = equity;

            maxLoss = Math.Max(maxLoss, peak - equity);

            if (trade.NetProfit < 0m)
            {
                consecutiveLosses++;
                maximumConsecutiveLosses = Math.Max(maximumConsecutiveLosses, consecutiveLosses);
            }
            else
            {
                consecutiveLosses = 0;
            }
        }

        maximumDrawdown = maxLoss;
        var averageHolding = trades.Count == 0
            ? TimeSpan.Zero
            : TimeSpan.FromTicks((long)trades.Average(x => x.HoldingDuration.Ticks));

        var netBySymbol = trades
            .GroupBy(x => x.Symbol, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(t => t.NetProfit), StringComparer.OrdinalIgnoreCase);

        var netByDate = trades
            .GroupBy(x => DateOnly.FromDateTime(x.ExitTimeUtc))
            .ToDictionary(x => x.Key, x => x.Sum(t => t.NetProfit));

        var peakUtilization = options.StartingCapital <= 0m
            ? 0m
            : peakCapitalDeployed / options.StartingCapital * 100m;

        return new PredictionTradingSimulationResult(
            predictionEvaluations,
            buySignals,
            holdDecisions,
            sellSignals,
            trades.Count,
            wins,
            losses,
            winRate,
            grossProfit,
            grossLoss,
            grossPnl,
            brokerage,
            Math.Max(0m, charges - brokerage),
            charges,
            netPnl,
            averageWin,
            averageLoss,
            profitFactor,
            maximumDrawdown,
            maximumConsecutiveLosses,
            averageHolding,
            options.StartingCapital,
            cash,
            peakCapitalDeployed,
            peakUtilization,
            totalEntryTurnover,
            replay.TrainingStartUtc,
            replay.TrainingEndUtc,
            replay.EvaluationStartUtc,
            replay.EvaluationEndUtc,
            replay.Evaluation,
            netBySymbol,
            netByDate,
            trades);
    }

    private async Task<int> CalculateQuantityAsync(
        MarketTick tick,
        decimal entryPrice,
        decimal stop,
        decimal capitalForTrade,
        PredictionTradingSimulationOptions options,
        CancellationToken cancellationToken)
    {
        var capitalQuantity = (int)Math.Floor(capitalForTrade / entryPrice);
        var riskPerShare = entryPrice - stop;

        if (capitalQuantity <= 0 || riskPerShare <= 0m)
            return 0;

        var quantity = Math.Min(
            capitalQuantity,
            (int)Math.Floor(options.MaximumRiskPerTrade / riskPerShare));

        while (quantity > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var entry = await _brokerage.CalculateAsync(
                tick.TradingSymbol,
                tick.SymbolToken,
                entryPrice,
                quantity,
                BrokerageTransactionType.Buy,
                "NSE",
                "INTRADAY");

            var exit = await _brokerage.CalculateAsync(
                tick.TradingSymbol,
                tick.SymbolToken,
                stop,
                quantity,
                BrokerageTransactionType.Sell,
                "NSE",
                "INTRADAY");

            var chargeInclusiveRisk = riskPerShare * quantity + entry.TotalCharges + exit.TotalCharges;

            if (chargeInclusiveRisk <= options.MaximumRiskPerTrade)
                return quantity;

            quantity--;
        }

        return 0;
    }

    private async Task ClosePositionAsync(
        OpenPosition position,
        MarketTick tick,
        double exitProbability,
        string exitCategory,
        string exitReason,
        decimal executableExit,
        List<PredictionTradingSimulationTrade> trades,
        CancellationToken cancellationToken)
    {
        if (executableExit <= 0m)
            return;

        var exitCharges = await _brokerage.CalculateAsync(
            tick.TradingSymbol,
            tick.SymbolToken,
            executableExit,
            position.Quantity,
            BrokerageTransactionType.Sell,
            "NSE",
            "INTRADAY");

        var gross = (executableExit - position.EntryPrice) * position.Quantity;
        var totalCharges = position.EntryCharges + exitCharges.TotalCharges;
        var net = gross - totalCharges;

        trades.Add(new PredictionTradingSimulationTrade(
            position.Symbol,
            position.SymbolToken,
            position.EntryTimeUtc,
            position.EntryPrice,
            position.EntryBid,
            position.EntryAsk,
            position.EntryProbability,
            "PREDICTION_BUY",
            position.Quantity,
            position.StopLoss,
            tick.ExchangeTime,
            executableExit,
            tick.Bid,
            tick.Ask,
            exitProbability,
            exitCategory,
            exitReason,
            gross,
            position.EntryCharges + exitCharges.Brokerage,
            totalCharges,
            net,
            tick.ExchangeTime - position.EntryTimeUtc));
    }

    private static void UpdateTrailingStop(OpenPosition position, MarketTick tick, decimal executableExit)
    {
        if (executableExit <= 0m)
            return;

        if (executableExit > position.HighestExecutableExit)
            position.HighestExecutableExit = executableExit;

        if (executableExit > position.EntryPrice + position.EntryCharges / Math.Max(1, position.Quantity))
        {
            var breakEven = position.EntryPrice + position.EntryCharges / Math.Max(1, position.Quantity);
            position.StopLoss = Math.Max(position.StopLoss, breakEven);
        }

        // Reuse the configured protective distance as the trailing distance. This is
        // deliberately simple and is a safety mechanism, not a prediction feature.
        var trailing = position.HighestExecutableExit *
                       (1m - position.ProtectiveStopPercent / 100m);

        position.StopLoss = Math.Max(position.StopLoss, trailing);
    }

    private static bool HasExecutableQuote(MarketTick tick) =>
        tick.Bid > 0m &&
        tick.Ask > 0m &&
        tick.Ask >= tick.Bid;

    private sealed class OpenPosition
    {
        public OpenPosition(
            string symbolToken,
            string symbol,
            DateTime entryTimeUtc,
            decimal entryPrice,
            decimal entryBid,
            decimal entryAsk,
            int quantity,
            decimal stopLoss,
            double entryProbability,
            decimal entryCharges,
            decimal entryCashReserved,
            decimal protectiveStopPercent)
        {
            SymbolToken = symbolToken;
            Symbol = symbol;
            EntryTimeUtc = entryTimeUtc;
            EntryPrice = entryPrice;
            EntryBid = entryBid;
            EntryAsk = entryAsk;
            Quantity = quantity;
            StopLoss = stopLoss;
            EntryProbability = entryProbability;
            EntryCharges = entryCharges;
            EntryCashReserved = entryCashReserved;
            ProtectiveStopPercent = protectiveStopPercent;
            HighestExecutableExit = entryPrice;
        }

        public string SymbolToken { get; }
        public string Symbol { get; }
        public DateTime EntryTimeUtc { get; }
        public decimal EntryPrice { get; }
        public decimal EntryBid { get; }
        public decimal EntryAsk { get; }
        public int Quantity { get; }
        public decimal StopLoss { get; set; }
        public decimal ProtectiveStopPercent { get; private set; }
        public double EntryProbability { get; }
        public decimal EntryCharges { get; }
        public decimal EntryCashReserved { get; }
        public decimal HighestExecutableExit { get; set; }
    }

    private static async IAsyncEnumerable<MarketTick> ReadTicksAsync(
        string path,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var outer = ZipFile.OpenRead(path);

        var direct = outer.GetEntry("market-ticks.jsonl");
        if (direct != null)
        {
            await foreach (var tick in ReadJsonLinesAsync(direct.Open(), cancellationToken))
                yield return tick;

            yield break;
        }

        var nested = outer.Entries.FirstOrDefault(x =>
            x.FullName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase));

        if (nested == null)
            throw new InvalidDataException(
                "ZIP does not contain market-ticks.jsonl or a nested ZIP archive.");

        var temp = Path.Combine(
            Path.GetTempPath(),
            "codename399-prediction-trading-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            await using (var source = nested.Open())
            await using (var target = new FileStream(
                temp,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1024 * 1024,
                true))
            {
                await source.CopyToAsync(target, cancellationToken);
            }

            using var inner = ZipFile.OpenRead(temp);
            var entry = inner.GetEntry("market-ticks.jsonl")
                ?? throw new InvalidDataException("Nested ZIP does not contain market-ticks.jsonl.");

            await foreach (var tick in ReadJsonLinesAsync(entry.Open(), cancellationToken))
                yield return tick;
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
            if (string.IsNullOrWhiteSpace(line))
                continue;

            RawMarketTickRecord? r;

            try
            {
                r = JsonSerializer.Deserialize<RawMarketTickRecord>(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (r == null || r.Ltp <= 0m || string.IsNullOrWhiteSpace(r.SymbolToken))
                continue;

            var normalizedTick = new MarketTick
            {
                TradingSymbol = r.TradingSymbol,
                SymbolToken = r.SymbolToken,
                ExchangeType = r.ExchangeType,
                SequenceNumber = r.SequenceNumber,
                ExchangeTime = r.ExchangeTimeUtc,
                LastUpdated = r.ReceivedTimeUtc,
                Ltp = r.Ltp,
                Bid = r.Bid,
                Ask = r.Ask,
                Open = r.Open,
                High = r.High,
                Low = r.Low,
                Close = r.Close,
                PreviousClose = r.PreviousClose,
                LastTradedQuantity = r.LastTradedQuantity,
                AveragePrice = r.AveragePrice,
                DayVolume = r.DayVolume,
                Volume = r.Volume,
                TotalBuyQuantity = r.TotalBuyQuantity,
                TotalSellQuantity = r.TotalSellQuantity,
                BidDepth = r.BidDepth.ToList(),
                AskDepth = r.AskDepth.ToList()
            };
            PredictionMarketDataNormalizer.NormalizeLegacyArchiveQuote(normalizedTick);
            yield return normalizedTick;
        }
    }
}
