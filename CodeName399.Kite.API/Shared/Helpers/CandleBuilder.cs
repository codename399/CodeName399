using CodeName399.Kite.API.Models.Indicator;

namespace CodeName399.Kite.API.Helpers;

public static class CandleBuilder
{
    public static List<Candle> Build5Minute(IEnumerable<Candle> oneMinuteCandles)
    {
        if (oneMinuteCandles == null)
            return [];

        return oneMinuteCandles
            .OrderBy(x => x.Timestamp)
            .GroupBy(x => Get5MinuteStart(x.Timestamp))
            .Select(CreateMergedCandle)
            .ToList();
    }

    public static void UpdateLatest5Minute(List<Candle> fiveMinuteCandles, Candle latestOneMinute)
    {
        var bucket = Get5MinuteStart(latestOneMinute.Timestamp);

        if (fiveMinuteCandles.Count == 0)
        {
            fiveMinuteCandles.Add(Clone(latestOneMinute, bucket));
            return;
        }

        var last = fiveMinuteCandles[^1];

        //
        // Same 5-minute candle.
        //
        if (last.Timestamp == bucket)
        {
            last.High = Math.Max(last.High, latestOneMinute.High);
            last.Low = Math.Min(last.Low, latestOneMinute.Low);
            last.Close = latestOneMinute.Close;
            last.Volume += latestOneMinute.Volume;

            return;
        }

        //
        // Ignore stale ticks.
        //
        if (last.Timestamp > bucket)
            return;

        //
        // New 5-minute candle.
        //
        fiveMinuteCandles.Add(Clone(latestOneMinute, bucket));

        const int MaxCandles = 500;

        if (fiveMinuteCandles.Count > MaxCandles)
            fiveMinuteCandles.RemoveAt(0);
    }




    public static List<Candle> Merge(IEnumerable<Candle> candles, TimeSpan interval)
    {
        if (candles == null)
            return [];

        return candles
            .OrderBy(x => x.Timestamp)
            .GroupBy(x => Floor(x.Timestamp, interval))
            .Select(CreateMergedCandle)
            .ToList();
    }

    private static Candle CreateMergedCandle(IGrouping<DateTime, Candle> group)
    {
        var candles = group.ToList();

        return new Candle
        {
            Timestamp = group.Key,
            Open = candles.First().Open,
            High = candles.Max(x => x.High),
            Low = candles.Min(x => x.Low),
            Close = candles.Last().Close,
            Volume = candles.Sum(x => x.Volume),
        };
    }

    private static Candle Clone(Candle candle, DateTime timestamp)
    {
        return new Candle
        {
            Timestamp = timestamp,
            Open = candle.Open,
            High = candle.High,
            Low = candle.Low,
            Close = candle.Close,
            Volume = candle.Volume,
        };
    }

    public static DateTime Get5MinuteStart(DateTime time)
    {
        return new DateTime(
            time.Year,
            time.Month,
            time.Day,
            time.Hour,
            (time.Minute / 5) * 5,
            0,
            time.Kind
        );
    }

    private static DateTime Floor(DateTime value, TimeSpan interval)
    {
        long ticks = value.Ticks / interval.Ticks;
        return new DateTime(ticks * interval.Ticks, value.Kind);
    }
}
