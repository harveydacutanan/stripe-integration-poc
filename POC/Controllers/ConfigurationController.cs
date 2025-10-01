using Microsoft.AspNetCore.Mvc;

namespace POC.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ConfigurationController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<ConfigurationController> _logger;

    public ConfigurationController(IConfiguration configuration, ILogger<ConfigurationController> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Gets public configuration settings for the frontend
    /// Only returns safe-to-expose configuration values
    /// </summary>
    [HttpGet("stripe")]
    public IActionResult GetStripeConfiguration()
    {
        try
        {
            var publishableKey = _configuration["Stripe:PublishableKey"];

            if (string.IsNullOrEmpty(publishableKey))
            {
                _logger.LogWarning("Stripe publishable key not configured");
                return BadRequest(new { error = "Stripe configuration not found" });
            }

            // Only return the publishable key (safe to expose)
            var config = new
            {
                publishableKey = publishableKey,
                environment = publishableKey.StartsWith("pk_live_") ? "live" : "test"
            };

            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving Stripe configuration");
            return StatusCode(500, new { error = "Configuration error" });
        }
    }

    /// <summary>
    /// Gets general application configuration for the frontend
    /// </summary>
    [HttpGet("app")]
    public IActionResult GetAppConfiguration()
    {
        try
        {
            var config = new
            {
                appName = "Stripe Payment POC",
                version = "1.0.0",
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development",
                supportedCurrencies = new[] { "aud", "usd", "eur", "gbp" },
                features = new
                {
                    customerRegistration = true,
                    savedPaymentMethods = true,
                    guestPayments = true,
                    multiplePaymentMethods = true
                }
            };

            return Ok(config);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving app configuration");
            return StatusCode(500, new { error = "Configuration error" });
        }
    }
}