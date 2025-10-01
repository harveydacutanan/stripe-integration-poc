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
}

public class SavedPaymentRequest
{
    public string CustomerId { get; set; } = string.Empty;
    public string PaymentMethodId { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "usd";
}