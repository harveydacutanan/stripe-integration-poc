using Microsoft.AspNetCore.Mvc;
using POC.Models;
using POC.Services;
using Stripe;

namespace POC.Controllers;

/// <summary>
/// Backend for the Apple Pay / Google Pay repayments POC.
/// Every endpoint here is wallet-agnostic - the wallet only decides how the card
/// reaches Stripe, so the same PaymentIntent lifecycle serves all card-based methods.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RepaymentController : ControllerBase
{
    private readonly RepaymentService _repaymentService;
    private readonly Services.CustomerService _customerService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<RepaymentController> _logger;

    public RepaymentController(
        RepaymentService repaymentService,
        Services.CustomerService customerService,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<RepaymentController> logger)
    {
        _repaymentService = repaymentService;
        _customerService = customerService;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    /// <summary>
    /// Front-end bootstrap: publishable key, merchant identity and surcharge rate.
    /// The Express Checkout Element needs the merchant country before it can offer wallets.
    /// </summary>
    [HttpGet("config")]
    public ActionResult<RepaymentConfigResponse> GetConfiguration()
    {
        var publishableKey = _configuration["Stripe:PublishableKey"];

        if (string.IsNullOrEmpty(publishableKey))
        {
            _logger.LogWarning("Stripe publishable key not configured");
            return BadRequest(new { error = "Stripe configuration not found" });
        }

        var domainAssociationPath = Path.Combine(
            _environment.WebRootPath ?? string.Empty,
            ".well-known",
            "apple-developer-merchantid-domain-association");

        return Ok(new RepaymentConfigResponse
        {
            PublishableKey = publishableKey,
            Environment = publishableKey.StartsWith("pk_live_") ? "live" : "test",
            MerchantName = _repaymentService.MerchantName,
            MerchantCountryCode = _repaymentService.MerchantCountryCode,
            Currency = _repaymentService.Currency,
            CardSurchargeRate = _repaymentService.CardSurchargeRate,
            IsApplePayDomainFileConfigured = System.IO.File.Exists(domainAssociationPath)
        });
    }

    /// <summary>
    /// Repayments root page data - account status card and bank transfer details.
    /// </summary>
    [HttpGet("account/{customerId}")]
    public async Task<ActionResult<RepaymentAccountSummary>> GetAccountSummary(string customerId)
    {
        try
        {
            var customerName = customerId;

            try
            {
                var customer = await _customerService.GetCustomerWithPaymentMethodsAsync(customerId);
                customerName = customer?.Name ?? customer?.Email ?? customerId;
            }
            catch (StripeException ex)
            {
                _logger.LogWarning(ex, "Could not load Stripe customer {CustomerId} for account summary", customerId);
            }

            return Ok(_repaymentService.BuildAccountSummary(customerId, customerName));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building repayment account summary");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Surcharge breakdown for the Review and finalise step.
    /// The server owns the surcharge so the sheet total can never disagree with the intent.
    /// </summary>
    [HttpPost("quote")]
    public ActionResult<RepaymentQuoteResponse> GetQuote([FromBody] RepaymentQuoteRequest request)
    {
        try
        {
            if (request.AmountInCents <= 0)
            {
                return BadRequest(new { error = "Amount must be greater than zero" });
            }

            var method = RepaymentService.ParseMethod(request.Method);
            var currency = string.IsNullOrWhiteSpace(request.Currency)
                ? _repaymentService.Currency
                : request.Currency.ToLowerInvariant();

            return Ok(_repaymentService.BuildQuote(request.AmountInCents, method, currency));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error building repayment quote");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Step 1 of the payment sequence: the app asks the backend for a PaymentIntent
    /// before the Apple Pay / Google Pay sheet is presented.
    /// </summary>
    [HttpPost("create-intent")]
    public async Task<ActionResult<RepaymentIntentResponse>> CreateRepaymentIntent(
        [FromBody] RepaymentIntentRequest request)
    {
        try
        {
            if (request.AmountInCents <= 0)
            {
                return BadRequest(new { error = "Amount must be greater than zero" });
            }

            var response = await _repaymentService.CreateRepaymentIntentAsync(request);

            _logger.LogInformation(
                "Repayment intent {PaymentIntentId} created for method {Method}",
                response.PaymentIntentId, request.Method);

            return Ok(response);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error creating repayment intent");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating repayment intent");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Receipt / success screen data for one repayment, including which wallet was used.
    /// </summary>
    [HttpGet("receipt/{paymentIntentId}")]
    public async Task<ActionResult<RepaymentHistoryItem>> GetReceipt(string paymentIntentId)
    {
        try
        {
            return Ok(await _repaymentService.GetReceiptAsync(paymentIntentId));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error retrieving repayment receipt");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving repayment receipt");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Repayment history list for the repayments root page.
    /// </summary>
    [HttpGet("history/{customerId}")]
    public async Task<ActionResult<List<RepaymentHistoryItem>>> GetHistory(
        string customerId,
        [FromQuery] int limit = 20,
        [FromQuery] bool includeFailed = false)
    {
        try
        {
            return Ok(await _repaymentService.GetHistoryAsync(customerId, limit, includeFailed));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error retrieving repayment history");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving repayment history");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Translates a failed PaymentIntent into the customer-facing error modal copy.
    /// Called after a wallet decline so the sheet dismisses into the in-app error pattern
    /// rather than leaving the customer inside the native wallet sheet.
    /// </summary>
    [HttpGet("failure/{paymentIntentId}")]
    public async Task<ActionResult<RepaymentFailure>> GetFailure(string paymentIntentId)
    {
        try
        {
            var receipt = await _repaymentService.GetReceiptAsync(paymentIntentId);
            return Ok(receipt.Failure ?? _repaymentService.ResolveFailure(null));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error resolving repayment failure");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving repayment failure");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
