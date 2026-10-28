using System.Collections.Concurrent;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Services;

/// <summary>
/// Calculates EMA9/EMA21 from one-minute candles. A bullish crossover arms an entry;
/// entry is confirmed at the close of that one-minute candle only if EMA9 remains above EMA21.
/// </summary>
public sealed class EmaCrossoverStrategyService
{
    private const int FastPeriod = 9;
    private const int SlowPeriod = 21;
    private const int MaximumCandles = 500;
    private const decimal MinimumPullbackPercent = 0.05m;
    private const decimal RecoveryFromPullbackLowPercent = 0.02m;
    private const int ChoppinessPeriod = 14;
    private const decimal SidewaysChoppinessThreshold = 61.8m;
    private const int StochRsiRsiPeriod = 14;
    private const int StochRsiStochasticPeriod = 14;
    private const int StochRsiKSmoothing = 3;
    private const int StochRsiDSmoothing = 3;

    private readonly ConcurrentDictionary<string, SymbolState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly MarketClock _clock;
    private readonly ILogger<EmaCrossoverStrategyService> _logger;

    public EmaCrossoverStrategyService(MarketClock clock, ILogger<EmaCrossoverStrategyService> logger)
    {
        _clock = clock;
        _logger = logger;
    }

    /// <summary>Seed from available completed historical one-minute candles.</summary>
    public void SeedHistoricalCandles(string symbolToken, IReadOnlyList<Candle> candles)
    {
        if (string.IsNullOrWhiteSpace(symbolToken) || candles == null)
            return;

        var currentMinute = MinuteStart(_clock.Now);
        var valid = candles
            .Where(x => x.Close > 0m && x.Timestamp < currentMinute)
            .OrderBy(x => x.Timestamp)
            .GroupBy(x => x.Timestamp)
            .Select(g => g.Last())
            .TakeLast(MaximumCandles)
            .ToList();

        if (valid.Count < SlowPeriod)
            return;

        var state = _states.GetOrAdd(symbolToken, _ => new SymbolState { SymbolToken = symbolToken });
        lock (state.Sync)
        {
            if (state.HistoricalSeeded)
                return;

            // Subscriptions begin immediately so ticks can arrive before the
            // historical request completes. Merge history with any completed
            // live candles instead of refusing the seed because a live candle
            // already exists.
            var merged = valid
                .Concat(state.CompletedCandles.Where(x => x.Close > 0m))
                .OrderBy(x => x.Timestamp)
                .GroupBy(x => x.Timestamp)
                .Select(g => g.Last())
                .TakeLast(MaximumCandles)
                .ToList();

            state.CompletedCandles.Clear();
            state.CompletedCandles.AddRange(merged.Select(Clone));
            var closes = merged.Select(x => x.Close).ToList();
            state.Ema9 = CalculateEma(closes, FastPeriod);
            state.Ema21 = CalculateEma(closes, SlowPeriod);
            state.PreviousEma9 = state.Ema9;
            state.PreviousEma21 = state.Ema21;
            state.GapPercent = state.Ema21 == 0m ? 0m : (state.Ema9 - state.Ema21) / state.Ema21 * 100m;
            state.HasPreviousEma = true;
            state.HistoricalSeeded = true;

            _logger.LogInformation(
                "EMA HISTORICAL SEED Token={Token} CandleCount={CandleCount} LastCandleIST={LastCandleIST} EMA9={EMA9:F8} EMA21={EMA21:F8}",
                symbolToken, valid.Count, valid[^1].Timestamp.ToString("yyyy-MM-dd HH:mm:ss"), state.Ema9, state.Ema21);
        }
    }

    public void OnTick(MarketTick tick)
    {
        if (string.IsNullOrWhiteSpace(tick.SymbolToken) || tick.Ltp <= 0m)
        {
            _logger.LogDebug(
                "EMA LIVE-TICK REJECTED Reason={Reason} Token={Token} Symbol={Symbol} LTP={LTP} ExchangeTime={ExchangeTime} LastUpdated={LastUpdated}",
                string.IsNullOrWhiteSpace(tick.SymbolToken) ? "MissingSymbolToken" : "NonPositiveLtp",
                tick.SymbolToken, tick.TradingSymbol, tick.Ltp, tick.ExchangeTime, tick.LastUpdated);
            return;
        }

        var now = _clock.Now;
        var minute = MinuteStart(now);
        var state = _states.GetOrAdd(tick.SymbolToken, _ => new SymbolState { SymbolToken = tick.SymbolToken });

        lock (state.Sync)
        {
            var previousTickPrice = state.LastTickPrice;

            if (state.CurrentCandle == null)
            {
                state.CurrentCandle = NewCandle(minute, tick.Ltp);
                state.CurrentMinute = minute;
                _logger.LogInformation(
                    "EMA LIVE CANDLE STREAM STARTED Token={Token} Symbol={Symbol} StartMinuteIST={StartMinuteIST} InitialPrice={Price:F6} HistoricalSeed={HistoricalSeeded}",
                    tick.SymbolToken, tick.TradingSymbol, minute.ToString("yyyy-MM-dd HH:mm:ss"), tick.Ltp, state.HistoricalSeeded);
            }
            else if (state.CurrentCandle.Timestamp < minute)
            {
                var previousMinute = state.CurrentCandle.Timestamp;
                var sameTradingDay = previousMinute.Date == minute.Date;
                FinalizeCurrentCandle(state);
                ProcessClosedCandleEntryConfirmation(state, tick.SymbolToken, tick.TradingSymbol);

                if (!sameTradingDay)
                {
                    state.EntrySignalPending = false;
                    state.EntryRecoveryConfirmed = false;
                    state.PullbackObserved = false;
                    state.PostCrossoverHigh = 0m;
                    state.PullbackLow = 0m;
                    state.HasPreviousEma = false;
                    state.PreviousEma9 = 0m;
                    state.PreviousEma21 = 0m;
                }

                if (minute - previousMinute > TimeSpan.FromMinutes(1))
                {
                    _logger.LogWarning(
                        "EMA LIVE CANDLE GAP Token={Token} Symbol={Symbol} PreviousCandleIST={PreviousCandleIST} NextTickMinuteIST={NextTickMinuteIST} MissingMinutes={MissingMinutes} Action=DoNotInventMissingCandles",
                        tick.SymbolToken, tick.TradingSymbol,
                        previousMinute.ToString("yyyy-MM-dd HH:mm:ss"), minute.ToString("yyyy-MM-dd HH:mm:ss"),
                        Math.Max(0, (int)(minute - previousMinute).TotalMinutes - 1));
                }

                state.CurrentCandle = NewCandle(minute, tick.Ltp);
                state.CurrentMinute = minute;
            }

            var candle = state.CurrentCandle!;
            candle.High = Math.Max(candle.High, tick.Ltp);
            candle.Low = candle.Low <= 0m ? tick.Ltp : Math.Min(candle.Low, tick.Ltp);
            candle.Close = tick.Ltp;
            state.CurrentMinute = minute;

            // Keep exactly one provisional close for the active minute.
            var closes = state.CompletedCandles
                .Where(x => x.Close > 0m)
                .OrderBy(x => x.Timestamp)
                .Select(x => x.Close)
                .Append(candle.Close)
                .ToList();
            if (closes.Count == 0)
                return;

            var ema9 = CalculateEma(closes, FastPeriod);
            var ema21 = CalculateEma(closes, SlowPeriod);
            var previousEma9 = state.Ema9;
            var previousEma21 = state.Ema21;
            var wasTradeReady = state.HasPreviousEma;
            var isTradeReady = closes.Count >= SlowPeriod;
            var gapPercent = ema21 == 0m ? 0m : (ema9 - ema21) / ema21 * 100m;

            if (state.LastEmaLogMinute != minute)
            {
                _logger.LogInformation(
                    "EMA LIVE-TICK CALCULATION Token={Token} Symbol={Symbol} TimestampIST={TimestampIST} CandleCount={CandleCount} CompletedCandles={CompletedCandles} CurrentCandleClose={CurrentCandleClose:F6} EMA9={EMA9:F8} EMA21={EMA21:F8} GapPercent={GapPercent:F8} EntryReady={EntryReady} HistoricalSeeded={HistoricalSeeded}",
                    tick.SymbolToken, tick.TradingSymbol, now.ToString("yyyy-MM-dd HH:mm:ss.fff"), closes.Count,
                    state.CompletedCandles.Count, candle.Close, ema9, ema21, gapPercent,
                    isTradeReady ? "READY" : "WARMING_UP", state.HistoricalSeeded);
                state.LastEmaLogMinute = minute;
            }

            // A bullish crossover arms the setup; the completed candle must retain EMA9 > EMA21.
            if (wasTradeReady && previousEma9 <= previousEma21 && ema9 > ema21)
            {
                state.ExitSignalPending = false;
                state.EntrySignalPending = true;
                state.EntryRecoveryConfirmed = false;
                state.PullbackObserved = false;
                state.PostCrossoverHigh = tick.Ltp;
                state.PullbackLow = 0m;
                state.SignalCandleTime = minute;
                _logger.LogInformation(
                    "EMA BULLISH CROSSOVER ARMED Token={Token} Symbol={Symbol} TimestampIST={TimestampIST} Price={Price:F6} EMA9={EMA9:F8} EMA21={EMA21:F8} GapPercent={GapPercent:F8} Next=ConfirmAtCandleClose",
                    tick.SymbolToken, tick.TradingSymbol, now.ToString("yyyy-MM-dd HH:mm:ss.fff"), tick.Ltp, ema9, ema21, gapPercent);
            }

            // Confirm pullback recovery with market structure + Stochastic RSI;
            // Choppiness Index independently blocks sideways-market entries.
            var indicatorCandles = state.CompletedCandles
                .Where(x => x.Close > 0m)
                .Append(Clone(candle))
                .OrderBy(x => x.Timestamp)
                .GroupBy(x => x.Timestamp)
                .Select(g => g.Last())
                .TakeLast(MaximumCandles)
                .ToList();
            var choppiness = CalculateChoppiness(indicatorCandles, ChoppinessPeriod);
            var (stochK, stochD, previousStochK) = CalculateStochasticRsi(
                indicatorCandles, StochRsiRsiPeriod, StochRsiStochasticPeriod,
                StochRsiKSmoothing, StochRsiDSmoothing);
            var sideways = choppiness >= SidewaysChoppinessThreshold;
            if (state.LastIndicatorLogMinute != minute)
            {
                _logger.LogInformation(
                    "ENTRY INDICATORS Token={Token} Symbol={Symbol} TimestampIST={TimestampIST} EMA9={EMA9:F8} EMA21={EMA21:F8} CHOP14={CHOP:F2} CHOPThreshold={CHOPThreshold:F2} Sideways={Sideways} StochRSI_K={StochK:F2} StochRSI_D={StochD:F2} PreviousStochRSI_K={PreviousStochK:F2} IndicatorHistoryCandles={CandleCount}",
                    tick.SymbolToken, tick.TradingSymbol, now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    ema9, ema21, choppiness, SidewaysChoppinessThreshold, sideways,
                    stochK, stochD, previousStochK, indicatorCandles.Count);
                state.LastIndicatorLogMinute = minute;
            }

            // Pullback and recovery entry confirmation is deliberately NOT evaluated
            // from live ticks. It is evaluated only after a one-minute candle closes.
            // This prevents transient intrabar moves from triggering an entry.

            // EMA bearish crossover is not an exit trigger. Exits are managed by
            // charge-aware protective/trailing stops in SellTradingService.

            state.PreviousEma9 = ema9;
            state.PreviousEma21 = ema21;
            state.Ema9 = ema9;
            state.Ema21 = ema21;
            state.GapPercent = gapPercent;
            state.HasPreviousEma = isTradeReady;
            state.LastTickPrice = tick.Ltp;
        }
    }

    private void ProcessClosedCandleEntryConfirmation(SymbolState state, string symbolToken, string symbol)
    {
        var candle = state.CompletedCandles.LastOrDefault(x => x.Close > 0m);
        if (candle == null || !state.EntrySignalPending || state.EntryRecoveryConfirmed)
            return;

        var closes = state.CompletedCandles
            .Where(x => x.Close > 0m)
            .TakeLast(MaximumCandles)
            .Select(x => x.Close)
            .ToList();

        if (closes.Count < SlowPeriod)
            return;

        // Entry is confirmed on the completed crossover candle, but only if
        // EMA9 still holds above EMA21 at candle close. No pullback, recovery,
        // StochRSI, or sideways-market gate is required.
        var ema9 = CalculateEma(closes, FastPeriod);
        var ema21 = CalculateEma(closes, SlowPeriod);
        var closeTime = candle.Timestamp.AddMinutes(1);

        if (ema9 <= ema21)
        {
            state.EntrySignalPending = false;
            state.EntryRecoveryConfirmed = false;
            state.PullbackObserved = false;
            state.PostCrossoverHigh = 0m;
            state.PullbackLow = 0m;

            _logger.LogInformation(
                "EMA ENTRY SETUP INVALIDATED ON CANDLE CLOSE Token={Token} Symbol={Symbol} CandleCloseIST={CandleCloseIST} Reason=EMA9NotAboveEMA21 EMA9={EMA9:F8} EMA21={EMA21:F8}",
                symbolToken, symbol, closeTime.ToString("yyyy-MM-dd HH:mm:ss"), ema9, ema21);
            return;
        }

        var candles = state.CompletedCandles
            .Where(x => x.Close > 0m)
            .TakeLast(MaximumCandles)
            .ToList();
        var choppiness = CalculateChoppiness(candles, ChoppinessPeriod);
        var (stochK, stochD, _) = CalculateStochasticRsi(
            candles, StochRsiRsiPeriod, StochRsiStochasticPeriod,
            StochRsiKSmoothing, StochRsiDSmoothing);

        state.EntryRecoveryConfirmed = true;
        state.EntrySignalPrice = candle.Close;
        state.EntrySignalTime = closeTime;
        state.EntrySignalEma9 = ema9;
        state.EntrySignalEma21 = ema21;
        state.EntrySignalGapPercent = ema21 == 0m ? 0m : (ema9 - ema21) / ema21 * 100m;
        state.EntryChoppiness = choppiness;
        state.EntryStochRsiK = stochK;
        state.EntryStochRsiD = stochD;
        state.EntryPullbackLow = 0m;
        state.EntryRecoverySwingHigh = 0m;

        _logger.LogInformation(
            "EMA CROSSOVER ENTRY CONFIRMED ON CANDLE CLOSE Token={Token} Symbol={Symbol} CandleCloseIST={CandleCloseIST} EntryReferencePrice={Price:F6} EMA9={EMA9:F8} EMA21={EMA21:F8} GapPercent={GapPercent:F8} CHOP14={CHOP:F2} StochRSI_K={StochK:F2} StochRSI_D={StochD:F2} EntryRule=EMA9AboveEMA21AtCandleClose",
            symbolToken, symbol, closeTime.ToString("yyyy-MM-dd HH:mm:ss"), candle.Close,
            ema9, ema21, state.EntrySignalGapPercent, choppiness, stochK, stochD);
    }

    public EmaCrossoverSnapshot? GetSnapshot(string symbolToken)
    {
        if (!_states.TryGetValue(symbolToken, out var state))
            return null;

        lock (state.Sync)
        {
            return new EmaCrossoverSnapshot(
                state.Ema9,
                state.Ema21,
                state.GapPercent,
                state.EntrySignalPending && !state.EntryRecoveryConfirmed,
                state.SignalCandleTime == default ? state.CurrentMinute : state.SignalCandleTime,
                state.EntryChoppiness,
                state.EntryStochRsiK,
                state.EntryStochRsiD,
                state.EntryPullbackLow,
                state.EntryRecoverySwingHigh);
        }
    }

    public bool TryConsumeExitSignal(string symbolToken, out EmaCrossoverSignal signal)
    {
        signal = default!;
        if (!_states.TryGetValue(symbolToken, out var state))
            return false;

        lock (state.Sync)
        {
            if (!state.ExitSignalPending || !state.HasPreviousEma)
                return false;

            signal = new EmaCrossoverSignal(
                state.Ema9,
                state.Ema21,
                state.GapPercent,
                state.ExitSignalCandleTime == default ? state.CurrentMinute : state.ExitSignalCandleTime);
            state.ExitSignalPending = false;
            return true;
        }
    }

    public bool TryConsumeEntrySignal(string symbolToken, out EmaCrossoverSignal signal)
    {
        signal = default!;
        if (!_states.TryGetValue(symbolToken, out var state))
            return false;

        lock (state.Sync)
        {
            // Pullback/recovery and all entry indicators were already validated
            // on the completed recovery candle. Do not re-check the unfinished
            // current candle here: that could invalidate a valid close-confirmed
            // signal during the delay before the buy worker consumes it.
            if (!state.EntrySignalPending || !state.EntryRecoveryConfirmed)
            {
                _logger.LogDebug(
                    "EMA ENTRY SIGNAL NOT READY Token={Token} Pending={Pending} RecoveryConfirmed={RecoveryConfirmed}",
                    symbolToken, state.EntrySignalPending, state.EntryRecoveryConfirmed);
                return false;
            }

            signal = new EmaCrossoverSignal(
                state.EntrySignalEma9,
                state.EntrySignalEma21,
                state.EntrySignalGapPercent,
                state.EntrySignalTime == default ? state.CurrentMinute : state.EntrySignalTime,
                state.EntryChoppiness,
                state.EntryStochRsiK,
                state.EntryStochRsiD,
                state.EntryPullbackLow,
                state.EntryRecoverySwingHigh);
            state.EntrySignalPending = false;
            state.EntryRecoveryConfirmed = false;
            state.PullbackObserved = false;
            return true;
        }
    }

    private bool IsSideways(SymbolState state, decimal currentPrice)
    {
        var candles = state.CompletedCandles.Where(x => x.Close > 0m)
            .Append(state.CurrentCandle is { Close: > 0m } current ? Clone(current) : null)
            .Where(x => x != null).Select(x => x!).TakeLast(MaximumCandles).ToList();
        if (candles.Count < ChoppinessPeriod)
            return false;
        return CalculateChoppiness(candles, ChoppinessPeriod) >= SidewaysChoppinessThreshold;
    }

    private static decimal CalculateChoppiness(IReadOnlyList<Candle> candles, int period)
    {
        if (period < 2 || candles.Count < period)
            return 100m; // fail closed: insufficient CHOP history must not allow an entry

        var window = candles.TakeLast(period).ToList();
        decimal trueRangeSum = 0m;
        for (var i = 0; i < window.Count; i++)
        {
            var c = window[i];
            var previousClose = i > 0 ? window[i - 1].Close : c.Close;
            trueRangeSum += Math.Max(c.High - c.Low,
                Math.Max(Math.Abs(c.High - previousClose), Math.Abs(c.Low - previousClose)));
        }

        var range = window.Max(x => x.High) - window.Min(x => x.Low);
        if (range <= 0m || trueRangeSum <= 0m) return 100m;
        var value = 100d * Math.Log10((double)(trueRangeSum / range)) / Math.Log10(period);
        return (decimal)Math.Clamp(value, 0d, 100d);
    }

    private static (decimal K, decimal D, decimal PreviousK) CalculateStochasticRsi(
        IReadOnlyList<Candle> candles, int rsiPeriod, int stochasticPeriod, int kSmoothing, int dSmoothing)
    {
        if (candles.Count < rsiPeriod + stochasticPeriod + kSmoothing + dSmoothing + 2)
            return (-1m, -1m, -1m);

        var closes = candles.Select(x => x.Close).ToList();
        var rsiValues = new List<decimal>();
        for (var end = rsiPeriod; end < closes.Count; end++)
        {
            decimal gains = 0m, losses = 0m;
            for (var i = end - rsiPeriod + 1; i <= end; i++)
            {
                var change = closes[i] - closes[i - 1];
                if (change > 0m) gains += change;
                else losses -= change;
            }

            var avgGain = gains / rsiPeriod;
            var avgLoss = losses / rsiPeriod;
            var rsi = avgGain == 0m && avgLoss == 0m ? 50m :
                avgLoss == 0m ? 100m :
                avgGain == 0m ? 0m :
                100m - (100m / (1m + avgGain / avgLoss));
            rsiValues.Add(rsi);
        }

        var raw = new List<decimal>();
        for (var i = stochasticPeriod - 1; i < rsiValues.Count; i++)
        {
            var window = rsiValues.Skip(i - stochasticPeriod + 1).Take(stochasticPeriod).ToList();
            var min = window.Min();
            var max = window.Max();
            raw.Add(max == min ? 50m : (rsiValues[i] - min) / (max - min) * 100m);
        }

        var kValues = new List<decimal>();
        for (var i = kSmoothing - 1; i < raw.Count; i++)
            kValues.Add(raw.Skip(i - kSmoothing + 1).Take(kSmoothing).Average());

        var dValues = new List<decimal>();
        for (var i = dSmoothing - 1; i < kValues.Count; i++)
            dValues.Add(kValues.Skip(i - dSmoothing + 1).Take(dSmoothing).Average());

        if (kValues.Count < 2 || dValues.Count == 0) return (-1m, -1m, -1m);
        return (kValues[^1], dValues[^1], kValues[^2]);
    }

    private void FinalizeCurrentCandle(SymbolState state)
    {
        var completed = state.CurrentCandle;
        if (completed == null || completed.Close <= 0m)
            return;

        state.CompletedCandles.Add(Clone(completed));
        if (state.CompletedCandles.Count > MaximumCandles)
            state.CompletedCandles.RemoveRange(0, state.CompletedCandles.Count - MaximumCandles);
    }

    private static decimal CalculateEma(IReadOnlyList<decimal> values, int period)
    {
        if (values.Count == 0 || period <= 0)
            return 0m;

        var multiplier = 2m / (period + 1m);
        decimal ema;
        var startIndex = 0;
        if (values.Count >= period)
        {
            ema = values.Take(period).Average();
            startIndex = period;
        }
        else
        {
            // Publish provisional values during warm-up, but don't signal a trade
            // until 21 distinct candle closes have been observed.
            ema = values[0];
            startIndex = 1;
        }

        for (var i = startIndex; i < values.Count; i++)
            ema = (values[i] - ema) * multiplier + ema;

        // Round the EMA values themselves (not only their display) so the exact
        // same six-decimal values drive crossover detection, snapshots and logs.
        return Math.Round(ema, 6, MidpointRounding.AwayFromZero);
    }

    private static DateTime MinuteStart(DateTime time) =>
        new(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0, time.Kind);

    private static Candle NewCandle(DateTime timestamp, decimal price) => new()
    {
        Timestamp = timestamp,
        Open = price,
        High = price,
        Low = price,
        Close = price
    };

    private static Candle Clone(Candle source) => new()
    {
        Timestamp = source.Timestamp,
        Open = source.Open,
        High = source.High,
        Low = source.Low,
        Close = source.Close,
        Volume = source.Volume
    };

    private sealed class SymbolState
    {
        public object Sync { get; } = new();
        public string SymbolToken { get; set; } = string.Empty;
        public List<Candle> CompletedCandles { get; } = [];
        public Candle? CurrentCandle { get; set; }
        public DateTime CurrentMinute { get; set; }
        public DateTime SignalCandleTime { get; set; }
        public DateTime LastEmaLogMinute { get; set; } = DateTime.MinValue;
        public DateTime LastIndicatorLogMinute { get; set; } = DateTime.MinValue;
        public decimal PreviousEma9 { get; set; }
        public decimal PreviousEma21 { get; set; }
        public decimal Ema9 { get; set; }
        public decimal Ema21 { get; set; }
        public decimal GapPercent { get; set; }
        public bool HasPreviousEma { get; set; }
        public bool HistoricalSeeded { get; set; }
        public bool EntrySignalPending { get; set; }
        public bool EntryRecoveryConfirmed { get; set; }
        public bool PullbackObserved { get; set; }
        public decimal PostCrossoverHigh { get; set; }
        public decimal PullbackLow { get; set; }
        public DateTime PullbackStartTime { get; set; }
        public decimal RecoverySwingHigh { get; set; }
        public decimal EntryChoppiness { get; set; }
        public decimal EntryStochRsiK { get; set; }
        public decimal EntryStochRsiD { get; set; }
        public decimal EntryPullbackLow { get; set; }
        public decimal EntryRecoverySwingHigh { get; set; }
        public decimal EntrySignalPrice { get; set; }
        public decimal EntrySignalEma9 { get; set; }
        public decimal EntrySignalEma21 { get; set; }
        public decimal EntrySignalGapPercent { get; set; }
        public DateTime EntrySignalTime { get; set; }
        public decimal LastTickPrice { get; set; }
        public bool ExitSignalPending { get; set; }
        public DateTime ExitSignalCandleTime { get; set; }
    }
}

public sealed record EmaCrossoverSnapshot(
    decimal Ema9,
    decimal Ema21,
    decimal GapPercent,
    bool EntrySignalPending,
    DateTime CandleTimeIst,
    decimal Choppiness = -1m,
    decimal StochRsiK = -1m,
    decimal StochRsiD = -1m,
    decimal PullbackLow = 0m,
    decimal RecoverySwingHigh = 0m);

public sealed record EmaCrossoverSignal(
    decimal Ema9,
    decimal Ema21,
    decimal GapPercent,
    DateTime CandleTimeIst,
    decimal Choppiness = -1m,
    decimal StochRsiK = -1m,
    decimal StochRsiD = -1m,
    decimal PullbackLow = 0m,
    decimal RecoverySwingHigh = 0m);
