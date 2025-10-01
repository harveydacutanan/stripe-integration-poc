using Stripe;
using POC.Models;

namespace POC.Services;

public class CustomerService
{
    private readonly Stripe.CustomerService _customerService;
    private readonly PaymentMethodService _paymentMethodService;
    private readonly SetupIntentService _setupIntentService;

    public CustomerService()
    {
        _customerService = new Stripe.CustomerService();
        _paymentMethodService = new PaymentMethodService();
        _setupIntentService = new SetupIntentService();
    }

    /// <summary>
    /// Creates a new Stripe customer
    /// </summary>
    public async Task<Customer> CreateCustomerAsync(CustomerRegistrationRequest request)
    {
        var options = new CustomerCreateOptions
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            Metadata = new Dictionary<string, string>
            {
                ["created_at"] = DateTime.UtcNow.ToString("O"),
                ["source"] = "POC_Application"
            }
        };

        return await _customerService.CreateAsync(options);
    }

    /// <summary>
    /// Retrieves customer with their saved payment methods
    /// </summary>
    public async Task<CustomerResponse?> GetCustomerWithPaymentMethodsAsync(string customerId)
    {
        try
        {
            var customer = await _customerService.GetAsync(customerId);
            var paymentMethods = await GetCustomerPaymentMethodsAsync(customerId);

            return new CustomerResponse
            {
                CustomerId = customer.Id,
                Name = customer.Name ?? string.Empty,
                Email = customer.Email ?? string.Empty,
                Phone = customer.Phone,
                SavedPaymentMethods = paymentMethods
            };
        }
        catch (StripeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Gets all saved payment methods for a customer
    /// </summary>
    public async Task<List<SavedPaymentMethod>> GetCustomerPaymentMethodsAsync(string customerId)
    {
        var options = new PaymentMethodListOptions
        {
            Customer = customerId,
            Type = "card"
        };

        var paymentMethods = await _paymentMethodService.ListAsync(options);

        return paymentMethods.Select(pm => new SavedPaymentMethod
        {
            Id = pm.Id,
            Type = pm.Type,
            Last4 = pm.Card?.Last4 ?? string.Empty,
            Brand = pm.Card?.Brand ?? string.Empty,
            ExpMonth = (int)(pm.Card?.ExpMonth ?? 0),
            ExpYear = (int)(pm.Card?.ExpYear ?? 0)
        }).ToList();
    }

    /// <summary>
    /// Creates a setup intent for saving a payment method
    /// </summary>
    public async Task<SetupIntent> CreateSetupIntentAsync(string customerId)
    {
        var options = new SetupIntentCreateOptions
        {
            Customer = customerId,
            PaymentMethodTypes = new List<string> { "card" },
            Usage = "off_session",
            Metadata = new Dictionary<string, string>
            {
                ["customer_id"] = customerId,
                ["created_at"] = DateTime.UtcNow.ToString("O")
            }
        };

        return await _setupIntentService.CreateAsync(options);
    }

    /// <summary>
    /// Finds customer by email address
    /// </summary>
    public async Task<Customer?> FindCustomerByEmailAsync(string email)
    {
        var options = new CustomerListOptions
        {
            Email = email,
            Limit = 1
        };

        var customers = await _customerService.ListAsync(options);
        return customers.FirstOrDefault();
    }

    /// <summary>
    /// Detaches (removes) a payment method from a customer
    /// </summary>
    public async Task<PaymentMethod> DetachPaymentMethodAsync(string paymentMethodId)
    {
        return await _paymentMethodService.DetachAsync(paymentMethodId);
    }

    /// <summary>
    /// Updates customer information
    /// </summary>
    public async Task<Customer> UpdateCustomerAsync(string customerId, CustomerRegistrationRequest request)
    {
        var options = new CustomerUpdateOptions
        {
            Name = request.Name,
            Email = request.Email,
            Phone = request.Phone,
            Metadata = new Dictionary<string, string>
            {
                ["updated_at"] = DateTime.UtcNow.ToString("O")
            }
        };

        return await _customerService.UpdateAsync(customerId, options);
    }
}