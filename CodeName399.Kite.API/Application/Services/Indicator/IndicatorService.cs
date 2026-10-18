using CodeName399.Kite.API.Diagnostics;
using CodeName399.Kite.API.Engine;
using CodeName399.Kite.API.Helpers;
using CodeName399.Kite.API.Indicators.Engine;
using CodeName399.Kite.API.Models;
using CodeName399.Kite.API.Models.Indicator;
using CodeName399.Kite.API.Services.Interface;

namespace CodeName399.Kite.API.Services.Indicator;

public class IndicatorService : IIndicatorService
{
    private readonly HistoricalCacheService _historicalCache;
    private readonly IMarketDataProvider _marketDataProvider;
    private readonly MarketClock _clock;
    private readonly IndicatorEngine _indicatorEngine;
    private readonly ILogger<IndicatorService> _logger;

    public IndicatorService(
        HistoricalCacheService historicalCache,
        IMarketDataProvider marketDataProvider,
        MarketClock clock,
        IndicatorEngine indicatorEngine,
        ILogger<IndicatorService> logger
    )
    {
        _historicalCache = historicalCache;
        _marketDataProvider = marketDataProvider;
        _clock = clock;
        _indicatorEngine = indicatorEngine;
        _logger = logger;
    }

    public async Task<IndicatorSnapshot?> GetAsync(MarketQuoteData stock, string jwtToken)
    {
        using var flow = TradingFlowActivity.Start(
            "trading.indicators", stock.TradingSymbol, stock.SymbolToken,
            stock.InstrumentType.ToString());

        if (!_historicalCache.TryGet(stock.SymbolToken, out var history))
        {
            _logger.LogDebug("FLOW STAGE Indicators.HistoryMissing Symbol={Symbol}, Token={Token}", stock.TradingSymbol, stock.SymbolToken);
            return null;
        }

        if (!history.IsReady)
        {
            _logger.LogDebug("FLOW STAGE Indicators.HistoryNotReady Symbol={Symbol}, Token={Token}", stock.TradingSymbol, stock.SymbolToken);
            return null;
        }

        var newMinuteCreated = UpdateLiveCandles(history, stock);

        //
        // History became stale (new trading day).
        // Wait for HistoricalCacheBackgroundService to refresh.
        //
        if (!history.IsReady)
        {
            _logger.LogDebug("FLOW STAGE Indicators.HistoryBecameStale Symbol={Symbol}, Token={Token}", stock.TradingSymbol, stock.SymbolToken);
            return null;
        }

        //
        // Same candle → reuse snapshot.
        //
        if (!newMinuteCreated && history.Snapshot != null)
        {
            if (HasRequiredTechnicalIndicators(history.Snapshot))
            {
                _logger.LogDebug("FLOW STAGE Indicators.ReuseSnapshot Symbol={Symbol}, Token={Token}", stock.TradingSymbol, stock.SymbolToken);
                return history.Snapshot;
            }

            // Do not perpetuate a legacy/partial zero snapshot. Recalculate from
            // the available candle history so a repaired deployment can recover
            // without requiring a full process restart or cache deletion.
            _logger.LogDebug("FLOW STAGE Indicators.InvalidCachedSnapshot Symbol={Symbol}, Token={Token}; recalculating.", stock.TradingSymbol, stock.SymbolToken);
        }

        history.LastPriceUpdate = DateTime.UtcNow;

        var tick = _marketDataProvider.Get(stock.SymbolToken);

        var context = new IndicatorContext
        {
            Symbol = stock.TradingSymbol,
            SymbolToken = stock.SymbolToken,

            CurrentPrice = tick?.Ltp ?? stock.Ltp,
            PreviousClose = stock.Close,
            Candles1m = history.Candles1m,
            Candles5m = history.Candles5m.Count > 1
                ? history.Candles5m.Take(history.Candles5m.Count - 1).ToList()
                : history.Candles5m,
        };

        var snapshot = _indicatorEngine.Calculate(context);

        // A cache can be marked ready while one or more indicator series are
        // still warming up. Do not publish a partially empty technical snapshot
        // as a valid evaluation snapshot. Returning null here keeps the scanner
        // in its existing "loading" path and prevents quote-only data from being
        // used for paper/live execution or BUY/SELL notifications.
        if (!HasRequiredTechnicalIndicators(snapshot))
        {
            _logger.LogDebug("FLOW STAGE Indicators.Incomplete Symbol={Symbol}, Token={Token}, ATR={ATR}, Candles1m={Candles1m}, Candles5m={Candles5m}",
                stock.TradingSymbol, stock.SymbolToken, snapshot.ATR, context.Candles1m.Count, context.Candles5m.Count);
            return null;
        }

        history.LastIndicatorUpdate = DateTime.UtcNow;
        history.UpdateSnapshot(snapshot);

        _logger.LogDebug("FLOW END Indicators Symbol={Symbol}, Token={Token}, NewMinute={NewMinute}, ATR={ATR}",
            stock.TradingSymbol, stock.SymbolToken, newMinuteCreated, snapshot.ATR);

        // Persist only when a new 1-minute candle was created. This keeps
        // crash recovery current without writing the history file on every
        // market tick.
        if (newMinuteCreated)
        {
            _historicalCache.Persist(history.SymbolToken);
        }

        return snapshot;
    }

    private static bool HasRequiredTechnicalIndicators(IndicatorSnapshot snapshot)
        => snapshot.CurrentPrice > 0m && snapshot.ATR > 0m;

    private static DateTime Get1MinuteStart(DateTime time)
    {
        return new DateTime(time.Year, time.Month, time.Day, time.Hour, time.Minute, 0);
    }

    private static DateTime Get5MinuteStart(DateTime time)
    {
        return new DateTime(time.Year, time.Month, time.Day, time.Hour, (time.Minute / 5) * 5, 0);
    }

    private static bool UpdateLiveCandle(
        List<Candle> candles,
        DateTime currentTime,
        decimal currentPrice,
        decimal incrementalVolume
    )
    {
        if (candles.Count == 0)
        {
            candles.Add(
                new Candle
                {
                    Timestamp = currentTime,
                    Open = currentPrice,
                    High = currentPrice,
                    Low = currentPrice,
                    Close = currentPrice,
                    Volume = incrementalVolume,
                }
            );

            return true;
        }

        var last = candles[^1];

        // Update the current candle
        if (last.Timestamp == currentTime)
        {
            if (currentPrice > last.High)
                last.High = currentPrice;

            if (currentPrice < last.Low)
                last.Low = currentPrice;

            last.Close = currentPrice;
            last.Volume += incrementalVolume;

            return false;
        }

        // Ignore stale or out-of-order ticks
        if (last.Timestamp > currentTime)
        {
            return false;
        }

        // Start a new candle
        candles.Add(
            new Candle
            {
                Timestamp = currentTime,
                Open = last.Close,
                High = currentPrice,
                Low = currentPrice,
                Close = currentPrice,
                Volume = incrementalVolume,
            }
        );

        const int MaxCandles = 500;

        if (candles.Count > MaxCandles)
        {
            candles.RemoveAt(0);
        }

        return true;
    }

    private static decimal ResolveCurrentDayVolume(MarketTick? tick, MarketQuoteData stock)
    {
        // Some websocket packets can carry a zero cumulative day-volume even
        // though the REST quote already has a valid cumulative trade volume.
        // Prefer the live value only when it is actually usable.
        if (tick?.DayVolume > 0)
            return tick.DayVolume;

        if (stock.TradeVolume > 0)
            return stock.TradeVolume;

        if (stock.DayVolume > 0)
            return stock.DayVolume;

        return 0m;
    }

    private bool UpdateLiveCandles(CachedHistoricalData history, MarketQuoteData stock)
    {
        var tick = _marketDataProvider.Get(stock.SymbolToken);

        var currentPrice = tick?.Ltp > 0m ? tick.Ltp : stock.Ltp;
        var currentDayVolume = ResolveCurrentDayVolume(tick, stock);

        // DayVolume is cumulative for the trading session. Convert it to the
        // delta since the previous tick before adding it to a one-minute candle.
        // Never let a zero websocket field erase a valid REST quote volume.
        decimal incrementalVolume = 0m;

        if (currentDayVolume > 0m)
        {
            if (!history.IsLiveVolumeInitialized)
            {
                // Establish the baseline. Do not dump the whole day's volume into
                // the first live candle after the historical cache is loaded.
                history.PreviousDayVolume = (long)currentDayVolume;
                history.IsLiveVolumeInitialized = true;
            }
            else
            {
                incrementalVolume = currentDayVolume - history.PreviousDayVolume;
                if (incrementalVolume < 0m)
                {
                    // Broker/session reset. Re-baseline rather than creating a
                    // negative or artificial positive candle volume.
                    incrementalVolume = 0m;
                }

                history.PreviousDayVolume = (long)currentDayVolume;
            }
        }

        history.CurrentPrice = currentPrice;
        var currentMinute = Get1MinuteStart(_clock.Now);

        //
        // New trading day detected.
        // DO NOT clear history.
        // Mark cache stale and wait for background refresh.
        //
        if (
            history.Candles1m.Count > 0
            && history.Candles1m.Last().Timestamp.Date != currentMinute.Date
        )
        {
            history.IsReady = false;

            return false;
        }

        //
        // Update live one-minute candle.
        //
        bool newMinuteCreated = UpdateLiveCandle(
            history.Candles1m,
            currentMinute,
            currentPrice,
            incrementalVolume
        );

        //
        // Same minute.
        //
        if (!newMinuteCreated)
        {
            if (history.Candles5m.Count > 0)
                CandleBuilder.UpdateLatest5Minute(history.Candles5m, history.Candles1m[^1]);
            return false;
        }

        //
        // New minute completed.
        //
        history.Candles5m = CandleBuilder.Build5Minute(history.Candles1m);
        const int MaxFiveMinuteCandles = 500;

        if (history.Candles5m.Count > MaxFiveMinuteCandles)
        {
            history.Candles5m = history.Candles5m.TakeLast(MaxFiveMinuteCandles).ToList();
        }

        history.Last1MinuteRefresh = DateTime.UtcNow;
        history.Last5MinuteRefresh = DateTime.UtcNow;

        return true;
    }
}
