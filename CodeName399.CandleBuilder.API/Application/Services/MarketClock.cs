namespace CodeName399.CandleBuilder.API.Services;

public sealed class MarketClock
{
    public static readonly TimeZoneInfo IndiaTimeZone = ResolveIndiaTimeZone();
    private readonly Func<DateTime> _nowProvider;

    public MarketClock(Func<DateTime>? nowProvider = null)
    {
        _nowProvider = nowProvider ?? (() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ResolveConfiguredTimeZone()));
    }

    public TimeSpan MarketOpenTime  = new TimeSpan(9, 15, 0);
    public TimeSpan MarketCloseTime  = new TimeSpan(15, 30, 0);
    public DateTime Now => _nowProvider();
    public DateTime Today => Now.Date;
    public DateTime MarketOpen => Today.Add(MarketOpenTime);
    public DateTime MarketClose => Today.Add(MarketCloseTime);
    public DateTime AutoSquareOffTime => MarketClose.AddMinutes(-5);

    public List<DateOnly> TradingHolidays = new();

    public bool IsTradingDay => Now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !(TradingHolidays ?? []).Contains(DateOnly.FromDateTime(Now));

    public bool IsMarketOpen => IsTradingDay &&
        Now.TimeOfDay >= MarketOpenTime &&
         Now.TimeOfDay <= MarketCloseTime;

    /// <summary>
    /// Returns whether live raw market-data recording is allowed by the actual
    /// exchange session. This intentionally ignores IgnoreMarketHours because
    /// that setting is a trading/test override and must not cause raw recording
    /// to continue outside market hours.
    /// </summary>
    public bool IsRawMarketDataRecordingOpen(DateTime? localNow = null)
    {
        var now = localNow ?? Now;
        var date = DateOnly.FromDateTime(now);
        var isTradingDay = now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
            && !(TradingHolidays ?? []).Contains(date);

        return isTradingDay
            && now.TimeOfDay >= MarketOpenTime
            && now.TimeOfDay <= MarketCloseTime;
    }

    public bool IsNewIntradayEntryWindowOpen => IsTradingDay
        && Now.TimeOfDay < GetEntryCutoffTime()
        && (Now.TimeOfDay >= MarketOpenTime);

    public DateTime Combine(DateOnly date, TimeSpan time) => date.ToDateTime(TimeOnly.FromTimeSpan(time));

    public TimeSpan GetSessionClose() => MarketCloseTime;

    public TimeSpan GetEntryCutoffTime()
        => MarketCloseTime - TimeSpan.FromMinutes(30);

    public TimeSpan GetAutoSquareOffTime()
        => MarketCloseTime - TimeSpan.FromMinutes(5);

    private TimeZoneInfo ResolveConfiguredTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch { return IndiaTimeZone; }
    }

    private static TimeZoneInfo ResolveIndiaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }
}
