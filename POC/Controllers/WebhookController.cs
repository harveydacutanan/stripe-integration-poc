using Microsoft.AspNetCore.Mvc;
using Stripe;
using POC.Services;
using POC.Models;
using System.Text;

namespace POC.Controllers;

[ApiController]
[Route("api/[controller]")]
public class WebhookController : ControllerBase
{
    private readonly WebhookService _webhookService;
    private readonly ILogger<WebhookController> _logger;
    private readonly IConfiguration _configuration;

    public WebhookController(
        WebhookService webhookService,
        ILogger<WebhookController> logger,
        IConfiguration configuration)
    {
        _webhookService = webhookService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Stripe webhook endpoint
    /// This endpoint receives all webhook events from Stripe
    /// </summary>
    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook()
    {
        try
        {
            // Read the request body
            var requestBody = await ReadRequestBodyAsync();

            if (string.IsNullOrEmpty(requestBody))
            {
                _logger.LogWarning("Received empty webhook payload");
                return BadRequest("Empty payload");
            }

            // Get the Stripe signature header
            var signature = Request.Headers["Stripe-Signature"].FirstOrDefault();

            if (string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("Missing Stripe signature header");
                return BadRequest("Missing signature");
            }

            _logger.LogInformation("Received webhook with signature: {Signature}", signature);

            // Validate webhook signature
            if (!_webhookService.ValidateWebhookSignature(requestBody, signature))
            {
                _logger.LogError("Webhook signature validation failed");
                return Unauthorized("Invalid signature");
            }

            // Construct the Stripe event
            var webhookSecret = _configuration["Stripe:WebhookSecret"];
            var stripeEvent = EventUtility.ConstructEvent(requestBody, signature, webhookSecret);

            _logger.LogInformation("Processing webhook event: {EventId} - {EventType}",
                stripeEvent.Id, stripeEvent.Type);

            // Process the webhook event
            var result = await _webhookService.ProcessWebhookEventAsync(stripeEvent);

            if (result.Success)
            {
                _logger.LogInformation("Webhook processed successfully: {EventId}", stripeEvent.Id);
                return Ok(new { message = result.Message, eventId = stripeEvent.Id });
            }
            else
            {
                _logger.LogError("Webhook processing failed: {EventId} - {Error}",
                    stripeEvent.Id, result.ErrorDetails);
                return StatusCode(500, new {
                    message = result.Message,
                    error = result.ErrorDetails,
                    eventId = stripeEvent.Id
                });
            }
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe exception in webhook processing");
            return BadRequest(new { error = "Webhook processing failed", details = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in webhook processing");
            return StatusCode(500, new { error = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Test endpoint to verify webhook configuration
    /// </summary>
    [HttpGet("test")]
    public IActionResult TestWebhook()
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        var isConfigured = !string.IsNullOrEmpty(webhookSecret);

        return Ok(new
        {
            message = "Webhook endpoint is active",
            configured = isConfigured,
            endpoint = "/api/webhook/stripe",
            timestamp = DateTime.UtcNow
        });
    }

    /// <summary>
    /// Get webhook configuration information (for development/debugging)
    /// </summary>
    [HttpGet("config")]
    public IActionResult GetWebhookConfig()
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        var hasSecret = !string.IsNullOrEmpty(webhookSecret);

        return Ok(new
        {
            hasWebhookSecret = hasSecret,
            webhookEndpoint = "/api/webhook/stripe",
            supportedEvents = new[]
            {
                Events.PaymentIntentSucceeded,
                Events.PaymentIntentPaymentFailed,
                Events.PaymentIntentRequiresAction,
                Events.SetupIntentSucceeded,
                Events.SetupIntentSetupFailed,
                Events.CustomerCreated,
                Events.CustomerUpdated,
                Events.CustomerDeleted,
                Events.PaymentMethodAttached,
                Events.PaymentMethodDetached,
                Events.InvoicePaymentSucceeded,
                Events.InvoicePaymentFailed
            }
        });
    }

    /// <summary>
    /// Simulate a webhook event (for testing purposes)
    /// </summary>
    [HttpPost("simulate/{eventType}")]
    public async Task<IActionResult> SimulateWebhookEvent(string eventType, [FromBody] object eventData)
    {
        try
        {
            _logger.LogInformation("Simulating webhook event: {EventType}", eventType);

            // This would typically create a mock Stripe event for testing
            // For now, just return a success response
            await Task.Delay(100);

            return Ok(new
            {
                message = $"Simulated webhook event: {eventType}",
                eventType = eventType,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating webhook event: {EventType}", eventType);
            return StatusCode(500, new { error = "Simulation failed", details = ex.Message });
        }
    }

    private async Task<string> ReadRequestBodyAsync()
    {
        try
        {
            using var reader = new StreamReader(Request.Body, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading request body");
            return string.Empty;
        }
    }
}