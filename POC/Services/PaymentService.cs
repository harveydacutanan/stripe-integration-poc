using Stripe;
using POC.Models;

namespace POC.Services;

public class PaymentService
{
    private readonly PaymentIntentService _paymentIntentService;
    private readonly CustomerService _customerService;

    public PaymentService(CustomerService customerService)
    {
        _paymentIntentService = new PaymentIntentService();
        _customerService = customerService;
    }

    /// <summary>
    /// Creates a payment intent for one-time payment (no customer)
    /// </summary>
    public async Task<PaymentIntent> CreateOneTimePaymentIntentAsync(decimal amount, string currency = "usd")
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100), // Convert to cents
            Currency = currency,
            PaymentMethodTypes = new List<string> { "card" },
            Metadata = new Dictionary<string, string>
            {
                ["payment_type"] = "one_time",
                ["created_at"] = DateTime.UtcNow.ToString("O")
            }
        };

        return await _paymentIntentService.CreateAsync(options);
    }

    /// <summary>
    /// Creates a payment intent with customer (with option to save payment method)
    /// </summary>
    public async Task<PaymentIntent> CreateCustomerPaymentIntentAsync(
        string customerId,
        decimal amount,
        bool savePaymentMethod,
        string currency = "usd")
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100), // Convert to cents
            Currency = currency,
            Customer = customerId,
            PaymentMethodTypes = new List<string> { "card" },
            Metadata = new Dictionary<string, string>
            {
                ["payment_type"] = "customer_payment",
                ["save_payment_method"] = savePaymentMethod.ToString(),
                ["created_at"] = DateTime.UtcNow.ToString("O")
            }
        };

        // If we want to save the payment method, set up future usage
        if (savePaymentMethod)
        {
            options.SetupFutureUsage = "off_session";
        }

        return await _paymentIntentService.CreateAsync(options);
    }

    /// <summary>
    /// Creates a payment intent using an existing saved payment method
    /// </summary>
    public async Task<PaymentIntent> CreatePaymentIntentWithSavedMethodAsync(
        string customerId,
        string paymentMethodId,
        decimal amount,
        string currency = "usd")
    {
        var options = new PaymentIntentCreateOptions
        {
            Amount = (long)(amount * 100),
            Currency = currency,
            Customer = customerId,
            PaymentMethod = paymentMethodId,
            ConfirmationMethod = "manual",
            Confirm = true,
            ReturnUrl = "https://your-website.com/return", // You might want to make this configurable
            Metadata = new Dictionary<string, string>
            {
                ["payment_type"] = "saved_method",
                ["payment_method_id"] = paymentMethodId,
                ["created_at"] = DateTime.UtcNow.ToString("O")
            }
        };

        return await _paymentIntentService.CreateAsync(options);
    }

    /// <summary>
    /// Confirms a payment intent (usually after 3D Secure authentication)
    /// </summary>
    public async Task<PaymentIntent> ConfirmPaymentIntentAsync(string paymentIntentId)
    {
        return await _paymentIntentService.ConfirmAsync(paymentIntentId);
    }

    /// <summary>
    /// Retrieves a payment intent by ID
    /// </summary>
    public async Task<PaymentIntent> GetPaymentIntentAsync(string paymentIntentId)
    {
        return await _paymentIntentService.GetAsync(paymentIntentId);
    }

    /// <summary>
    /// Processes a complete payment flow - creates customer if needed and payment intent
    /// </summary>
    public async Task<PaymentResponse> ProcessPaymentAsync(PaymentRequest request)
    {
        string customerId = request.ExistingCustomerId ?? string.Empty;

        // Create customer if new customer info provided
        if (request.Customer != null && string.IsNullOrEmpty(customerId))
        {
            var customerRequest = new CustomerRegistrationRequest
            {
                Name = request.Customer.Name,
                Email = request.Customer.Email,
                Phone = request.Customer.Phone
            };

            // Check if customer already exists by email
            var existingCustomer = await _customerService.FindCustomerByEmailAsync(request.Customer.Email);
            if (existingCustomer != null)
            {
                customerId = existingCustomer.Id;
            }
            else
            {
                var newCustomer = await _customerService.CreateCustomerAsync(customerRequest);
                customerId = newCustomer.Id;
            }
        }

        // Create payment intent
        PaymentIntent paymentIntent;

        if (string.IsNullOrEmpty(customerId))
        {
            // One-time payment without customer
            paymentIntent = await CreateOneTimePaymentIntentAsync(request.Amount, request.Currency);
        }
        else
        {
            // Payment with customer (with option to save payment method)
            paymentIntent = await CreateCustomerPaymentIntentAsync(
                customerId,
                request.Amount,
                request.SavePaymentMethod,
                request.Currency);
        }

        return new PaymentResponse
        {
            ClientSecret = paymentIntent.ClientSecret,
            CustomerId = customerId,
            PaymentIntentId = paymentIntent.Id
        };
    }
}