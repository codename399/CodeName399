using CodeName399.Kite.API.Models.Configuration;

namespace CodeName399.Kite.API.Services;

public sealed class MarketClock
{
    public static readonly TimeZoneInfo IndiaTimeZone = ResolveIndiaTimeZone();
    private readonly TradingSettingsProvider _settings;
    private readonly Func<DateTime> _nowProvider;

    public MarketClock(TradingSettingsProvider settings, Func<DateTime>? nowProvider = null)
    {
        _settings = settings;
        _nowProvider = nowProvider ?? (() =>
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, ResolveConfiguredTimeZone()));
    }

    public DateTime Now => _nowProvider();
    public DateTime Today => Now.Date;
    public DateTime MarketOpen => Today.Add(_settings.Current.MarketOpenTime);
    public DateTime MarketClose => Today.Add(_settings.Current.MarketCloseTime);
    public DateTime AutoSquareOffTime => MarketClose.AddMinutes(-5);

    public bool IsTradingDay => Now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !(_settings.Current.TradingHolidays ?? []).Contains(DateOnly.FromDateTime(Now));

    public bool IsMarketOpen => IsTradingDay &&
        Now.TimeOfDay >= _settings.Current.MarketOpenTime &&
         Now.TimeOfDay <= _settings.Current.MarketCloseTime;

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
            && !(_settings.Current.TradingHolidays ?? []).Contains(date);

        return isTradingDay
            && now.TimeOfDay >= _settings.Current.MarketOpenTime
            && now.TimeOfDay <= _settings.Current.MarketCloseTime;
    }

    public bool IsNewIntradayEntryWindowOpen => IsTradingDay
        && Now.TimeOfDay < GetEntryCutoffTime(_settings.Current)
        && (Now.TimeOfDay >= _settings.Current.MarketOpenTime);

    public DateTime Combine(DateOnly date, TimeSpan time) => date.ToDateTime(TimeOnly.FromTimeSpan(time));

    public TimeSpan GetSessionClose() => _settings.Current.MarketCloseTime;

    public TimeSpan GetEntryCutoffTime(TradingConfiguration settings, string? productType = null)
        => settings.IntradayCutoffTime;

    public TimeSpan GetAutoSquareOffTime(TradingConfiguration settings, string? productType = null)
        => settings.MarketCloseTime - TimeSpan.FromMinutes(5);

    private TimeZoneInfo ResolveConfiguredTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(_settings.Current.MarketTimeZoneId); }
        catch { return IndiaTimeZone; }
    }

    private static TimeZoneInfo ResolveIndiaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }
}
