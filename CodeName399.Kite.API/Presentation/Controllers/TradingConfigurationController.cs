using CodeName399.Kite.API.Models.Configuration;
using CodeName399.Kite.API.Repositories.Interface;
using CodeName399.Kite.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace CodeName399.Kite.API.Controllers
{
    [Authorize]
    public class TradingConfigurationController : ControllerBase
    {
        private readonly ITradingConfigurationRepository _repository;
        private readonly TradingSettingsProvider _provider;
        private readonly ILogger<TradingConfigurationController> _logger;

        public TradingConfigurationController(
            ITradingConfigurationRepository repository,
            TradingSettingsProvider provider,
            ILogger<TradingConfigurationController> logger)
        {
            _repository = repository;
            _provider = provider;
            _logger = logger;
        }

        [HttpGet("get-configuration")]
        public async Task<ActionResult<TradingConfiguration>> Get()
        {
            try
            {
                var configuration = await _repository.GetAsync();

                if (configuration == null)
                    return NotFound();

                _logger.LogInformation(JsonSerializer.Serialize(configuration));
                return Ok(configuration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve trading configuration.");
                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    "Failed to retrieve trading configuration.");
            }
        }

        [HttpPut("set-configuration")]
        public async Task<IActionResult> Update([FromBody] TradingConfiguration? configuration)
        {
            try
            {
                // Important: without [ApiController], model-binding failures can result in a
                // null action parameter while the controller still executes. Previously this
                // produced an empty HTTP 400, making the real JSON/model error invisible.
                if (!ModelState.IsValid)
                {
                    var errors = ModelState
                        .Where(x => x.Value?.Errors.Count > 0)
                        .ToDictionary(
                            x => x.Key,
                            x => x.Value!.Errors
                                .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage)
                                    ? e.Exception?.Message ?? "Invalid value."
                                    : e.ErrorMessage)
                                .ToArray());

                    _logger.LogWarning(
                        "Trading configuration rejected during model binding. Errors={Errors}",
                        JsonSerializer.Serialize(errors));

                    return BadRequest(new
                    {
                        success = false,
                        message = "Invalid trading configuration.",
                        errors
                    });
                }

                if (configuration == null)
                {
                    _logger.LogWarning("Trading configuration request body was null.");
                    return BadRequest(new
                    {
                        success = false,
                        message = "Trading configuration request body is null."
                    });
                }

                configuration.Id = "DEFAULT";
                TradingConfigurationValidator.Validate(configuration);

                await _repository.UpdateAsync(configuration);
                _provider.Update(configuration);

                _logger.LogInformation(
                    "Trading configuration updated successfully. Id={ConfigurationId}",
                    configuration.Id);

                return Ok(configuration);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Trading configuration failed validation.");
                return BadRequest(new { success = false, message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to update trading configuration.");

                return StatusCode(
                    StatusCodes.Status500InternalServerError,
                    new
                    {
                        success = false,
                        message = "Failed to update trading configuration."
                    });
            }
        }
    }
}
