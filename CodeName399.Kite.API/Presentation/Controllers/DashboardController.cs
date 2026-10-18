using CodeName399.Kite.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CodeName399.Kite.API.Controllers
{
    [Authorize]
    public class DashboardController : ControllerBase
    {
        private readonly DashboardService _dashboard;
        private readonly ILogger<DashboardController> _logger;

        public DashboardController(DashboardService dashboard, ILogger<DashboardController> logger)
        {
            _dashboard = dashboard;
            _logger = logger;
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboard()
        {
            try
            {
                var dashboard = await _dashboard.GetDashboardAsync();

                return Ok(dashboard);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load dashboard.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "Failed to load dashboard."
                );
            }
        }
    }
}
