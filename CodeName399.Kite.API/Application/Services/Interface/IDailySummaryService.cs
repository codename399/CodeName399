namespace CodeName399.Kite.API.Services.Interface;

using CodeName399.Kite.API.Models;

public interface IDailySummaryService
{
    Task<bool> SendDailySummaryAsync();
    Task<DailyTradingSummary> GetDailySummaryAsync(DateOnly date);
}
