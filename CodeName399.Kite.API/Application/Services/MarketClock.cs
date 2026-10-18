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
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, IndiaTimeZone));
    }

    public DateTime Now => _nowProvider();
    public DateTime Today => Now.Date;
    public bool IsTradingDay => Now.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
        && !_settings.Current.TradingHolidays.Contains(DateOnly.FromDateTime(Now));

    public bool IsMarketOpen => _settings.Current.IgnoreMarketHours ||
        (IsTradingDay && Now.TimeOfDay >= _settings.Current.MarketOpenTime &&
         Now.TimeOfDay < _settings.Current.MarketCloseTime);


    public bool IsNewIntradayEntryWindowOpen =>
        _settings.Current.IgnoreMarketHours ||
        (IsTradingDay && Now.TimeOfDay >= _settings.Current.MarketOpenTime &&
         Now.TimeOfDay < _settings.Current.IntradayEntryCutoffTime);

    public TimeSpan GetAutoSquareOffTime(TradingConfiguration settings, string? productType = null)
        => settings.MarketCloseTime;

    public TimeSpan GetSessionClose() => _settings.Current.MarketCloseTime;

    private static TimeZoneInfo ResolveIndiaTimeZone()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time"); }
    }
}
