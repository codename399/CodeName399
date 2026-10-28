using CodeName399.Kite.API.Services.Interface;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeName399.Kite.API.Controllers;

[Authorize]
[ApiController]
public sealed class TradingSummaryController : ControllerBase
{
    private readonly IDailySummaryService _summary;

    public TradingSummaryController(IDailySummaryService summary) => _summary = summary;

    /// <summary>Returns a compact, AI-ready trading summary for the requested IST date.</summary>
    [HttpGet("trading/daily-summary")]
    public async Task<IActionResult> GetDailySummary([FromQuery] DateOnly? date = null)
    {
        var requestedDate = date ?? DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata")));
        return Ok(await _summary.GetDailySummaryAsync(requestedDate));
    }
}
