using Microsoft.AspNetCore.Mvc;
using POC.Models;
using POC.Services;
using Stripe;

namespace POC.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PaymentController : ControllerBase
{
    private readonly PaymentService _paymentService;
    private readonly Services.CustomerService _customerService;
    private readonly ILogger<PaymentController> _logger;

    public PaymentController(
        PaymentService paymentService,
        Services.CustomerService customerService,
        ILogger<PaymentController> logger)
    {
        _paymentService = paymentService;
        _customerService = customerService;
        _logger = logger;
    }

    /// <summary>
    /// Gets payment method details from Stripe
    /// </summary>
    [HttpGet("payment-method/{paymentMethodId}")]
    public async Task<ActionResult> GetPaymentMethod(string paymentMethodId)
    {
        try
        {
            _logger.LogInformation("Retrieving payment method: {PaymentMethodId}", paymentMethodId);

            var service = new PaymentMethodService();
            var paymentMethod = await service.GetAsync(paymentMethodId);

            var response = new
            {
                id = paymentMethod.Id,
                type = paymentMethod.Type,
                created = paymentMethod.Created,
                customerId = paymentMethod.CustomerId,
                card = paymentMethod.Card != null ? new
                {
                    brand = paymentMethod.Card.Brand,
                    last4 = paymentMethod.Card.Last4,
                    expMonth = paymentMethod.Card.ExpMonth,
                    expYear = paymentMethod.Card.ExpYear,
                    funding = paymentMethod.Card.Funding,
                    country = paymentMethod.Card.Country,
                    fingerprint = paymentMethod.Card.Fingerprint
                } : null,
                billingDetails = paymentMethod.BillingDetails != null ? new
                {
                    name = paymentMethod.BillingDetails.Name,
                    email = paymentMethod.BillingDetails.Email,
                    phone = paymentMethod.BillingDetails.Phone,
                    address = paymentMethod.BillingDetails.Address
                } : null
            };

            return Ok(response);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error retrieving payment method");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment method");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Creates a payment intent for processing payment
    /// Supports both one-time payments and payments with customer creation
    /// </summary>
    [HttpPost("create-intent")]
    public async Task<ActionResult<PaymentResponse>> CreatePaymentIntent([FromBody] PaymentRequest request)
    {
        try
        {
            _logger.LogInformation("Creating payment intent for amount: {Amount}", request.Amount);

            if (request.Amount <= 0)
            {
                return BadRequest("Amount must be greater than zero");
            }

            var response = await _paymentService.ProcessPaymentAsync(request);

            _logger.LogInformation("Payment intent created successfully: {PaymentIntentId}", response.PaymentIntentId);

            return Ok(response);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error creating payment intent");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment intent");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Creates a payment intent using a saved payment method
    /// </summary>
    [HttpPost("create-intent-with-saved-method")]
    public async Task<ActionResult<PaymentResponse>> CreatePaymentIntentWithSavedMethod(
        [FromBody] SavedPaymentRequest request)
    {
        try
        {
            _logger.LogInformation("Creating payment intent with saved method for customer: {CustomerId}",
                request.CustomerId);

            if (request.Amount <= 0)
            {
                return BadRequest("Amount must be greater than zero");
            }

            var paymentIntent = await _paymentService.CreatePaymentIntentWithSavedMethodAsync(
                request.CustomerId,
                request.PaymentMethodId,
                request.Amount,
                request.Currency);

            var response = new PaymentResponse
            {
                ClientSecret = paymentIntent.ClientSecret,
                CustomerId = request.CustomerId,
                PaymentIntentId = paymentIntent.Id
            };

            return Ok(response);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error creating payment intent with saved method");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment intent with saved method");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Confirms a payment intent (for 3D Secure flows)
    /// </summary>
    [HttpPost("confirm-intent/{paymentIntentId}")]
    public async Task<ActionResult> ConfirmPaymentIntent(string paymentIntentId)
    {
        try
        {
            var paymentIntent = await _paymentService.ConfirmPaymentIntentAsync(paymentIntentId);
            return Ok(new { status = paymentIntent.Status });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error confirming payment intent");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error confirming payment intent");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Gets payment intent status
    /// </summary>
    [HttpGet("intent/{paymentIntentId}")]
    public async Task<ActionResult> GetPaymentIntent(string paymentIntentId)
    {
        try
        {
            var paymentIntent = await _paymentService.GetPaymentIntentAsync(paymentIntentId);
            return Ok(new
            {
                id = paymentIntent.Id,
                status = paymentIntent.Status,
                amount = paymentIntent.Amount,
                currency = paymentIntent.Currency
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error retrieving payment intent");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment intent");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Gets saved payment methods for a customer
    /// </summary>
    [HttpGet("saved-payment-methods/{customerId}")]
    public async Task<ActionResult> GetSavedPaymentMethods(string customerId)
    {
        try
        {
            _logger.LogInformation("Fetching saved payment methods for customer: {CustomerId}", customerId);

            var service = new PaymentMethodService();
            var options = new PaymentMethodListOptions
            {
                Customer = customerId,
                Type = "card",
            };

            var paymentMethods = await service.ListAsync(options);

            var response = paymentMethods.Data.Select(pm => new
            {
                id = pm.Id,
                brand = pm.Card.Brand,
                last4 = pm.Card.Last4,
                expMonth = pm.Card.ExpMonth,
                expYear = pm.Card.ExpYear,
                isDefault = false // You can implement default logic if needed
            });

            return Ok(response);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error retrieving payment methods");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving payment methods");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Detaches (removes) a payment method from a customer
    /// </summary>
    [HttpDelete("payment-method/{paymentMethodId}")]
    public async Task<ActionResult> DetachPaymentMethod(string paymentMethodId)
    {
        try
        {
            _logger.LogInformation("Detaching payment method: {PaymentMethodId}", paymentMethodId);

            var service = new PaymentMethodService();
            var paymentMethod = await service.DetachAsync(paymentMethodId);

            return Ok(new { success = true, message = "Payment method removed successfully" });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error detaching payment method");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error detaching payment method");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Creates a payment intent (simplified endpoint for ECA)
    /// </summary>
    [HttpPost("create-payment-intent")]
    public async Task<ActionResult> CreatePaymentIntent([FromBody] CreatePaymentIntentRequest request)
    {
        try
        {
            _logger.LogInformation("Creating payment intent for customer: {CustomerId}, amount: {Amount}",
                request.CustomerId, request.Amount);

            var options = new PaymentIntentCreateOptions
            {
                Amount = request.Amount,
                Currency = request.Currency ?? "aud",
                Customer = request.CustomerId,
                Description = request.Description,
            };

            // If payment method is provided, use it directly
            if (!string.IsNullOrEmpty(request.PaymentMethodId))
            {
                options.PaymentMethod = request.PaymentMethodId;
            }
            else
            {
                // Only use automatic payment methods when no specific method is provided
                options.AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                };
            }

            // Save payment method for future use if requested
            if (request.SavePaymentMethod)
            {
                options.SetupFutureUsage = "off_session";
            }

            var service = new PaymentIntentService();
            var paymentIntent = await service.CreateAsync(options);

            return Ok(new
            {
                clientSecret = paymentIntent.ClientSecret,
                paymentIntentId = paymentIntent.Id
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error creating payment intent");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating payment intent");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}

public class CreatePaymentIntentRequest
{
    public long Amount { get; set; }
    public string? Currency { get; set; }
    public string? CustomerId { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? Description { get; set; }
    public bool SavePaymentMethod { get; set; }
}

public class SavedPaymentRequest
{
    public string CustomerId { get; set; } = string.Empty;
    public string PaymentMethodId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "usd";
}