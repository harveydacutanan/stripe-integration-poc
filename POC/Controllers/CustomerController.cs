using Microsoft.AspNetCore.Mvc;
using POC.Models;
using POC.Services;
using Stripe;

namespace POC.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CustomerController : ControllerBase
{
    private readonly Services.CustomerService _customerService;
    private readonly ILogger<CustomerController> _logger;

    public CustomerController(Services.CustomerService customerService, ILogger<CustomerController> logger)
    {
        _customerService = customerService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a new customer
    /// </summary>
    [HttpPost("create")]
    public async Task<ActionResult<CustomerResponse>> CreateCustomer([FromBody] CustomerRegistrationRequest request)
    {
        try
        {
            _logger.LogInformation("Creating customer: {Email}", request.Email);

            // Check if customer already exists
            var existingCustomer = await _customerService.FindCustomerByEmailAsync(request.Email);
            if (existingCustomer != null)
            {
                _logger.LogInformation("Customer already exists: {Email}", request.Email);
                var existingResponse = await _customerService.GetCustomerWithPaymentMethodsAsync(existingCustomer.Id);
                return Ok(existingResponse);
            }

            var customer = await _customerService.CreateCustomerAsync(request);

            var response = new CustomerResponse
            {
                CustomerId = customer.Id,
                Name = customer.Name ?? string.Empty,
                Email = customer.Email ?? string.Empty,
                Phone = customer.Phone,
                SavedPaymentMethods = new List<SavedPaymentMethod>()
            };

            _logger.LogInformation("Customer created successfully: {CustomerId}", customer.Id);

            return Ok(response);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error creating customer");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating customer");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Gets customer details with saved payment methods
    /// </summary>
    [HttpGet("{customerId}")]
    public async Task<ActionResult<CustomerResponse>> GetCustomer(string customerId)
    {
        try
        {
            var customer = await _customerService.GetCustomerWithPaymentMethodsAsync(customerId);
            if (customer == null)
            {
                return NotFound("Customer not found");
            }

            return Ok(customer);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error retrieving customer");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving customer");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Finds customer by email
    /// </summary>
    [HttpGet("by-email/{email}")]
    public async Task<ActionResult<CustomerResponse>> GetCustomerByEmail(string email)
    {
        try
        {
            var customer = await _customerService.FindCustomerByEmailAsync(email);
            if (customer == null)
            {
                return NotFound("Customer not found");
            }

            var response = await _customerService.GetCustomerWithPaymentMethodsAsync(customer.Id);
            return Ok(response);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error finding customer by email");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error finding customer by email");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Creates a setup intent for saving a payment method
    /// </summary>
    [HttpPost("{customerId}/setup-intent")]
    public async Task<ActionResult<SetupIntentResponse>> CreateSetupIntent(string customerId)
    {
        try
        {
            _logger.LogInformation("Creating setup intent for customer: {CustomerId}", customerId);

            var setupIntent = await _customerService.CreateSetupIntentAsync(customerId);

            var response = new SetupIntentResponse
            {
                ClientSecret = setupIntent.ClientSecret,
                SetupIntentId = setupIntent.Id
            };

            return Ok(response);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error creating setup intent");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating setup intent");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Gets all saved payment methods for a customer
    /// </summary>
    [HttpGet("{customerId}/payment-methods")]
    public async Task<ActionResult<List<SavedPaymentMethod>>> GetPaymentMethods(string customerId)
    {
        try
        {
            var paymentMethods = await _customerService.GetCustomerPaymentMethodsAsync(customerId);
            return Ok(paymentMethods);
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
    /// Removes a saved payment method
    /// </summary>
    [HttpDelete("payment-method/{paymentMethodId}")]
    public async Task<ActionResult> RemovePaymentMethod(string paymentMethodId)
    {
        try
        {
            _logger.LogInformation("Removing payment method: {PaymentMethodId}", paymentMethodId);

            await _customerService.DetachPaymentMethodAsync(paymentMethodId);

            return Ok(new { message = "Payment method removed successfully" });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error removing payment method");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing payment method");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>
    /// Updates customer information
    /// </summary>
    [HttpPut("{customerId}")]
    public async Task<ActionResult<CustomerResponse>> UpdateCustomer(
        string customerId,
        [FromBody] CustomerRegistrationRequest request)
    {
        try
        {
            _logger.LogInformation("Updating customer: {CustomerId}", customerId);

            var customer = await _customerService.UpdateCustomerAsync(customerId, request);

            var response = new CustomerResponse
            {
                CustomerId = customer.Id,
                Name = customer.Name ?? string.Empty,
                Email = customer.Email ?? string.Empty,
                Phone = customer.Phone,
                SavedPaymentMethods = await _customerService.GetCustomerPaymentMethodsAsync(customerId)
            };

            return Ok(response);
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe error updating customer");
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating customer");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}