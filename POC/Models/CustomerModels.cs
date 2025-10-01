namespace POC.Models;

public class CustomerRegistrationRequest
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

public class PaymentRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "usd";
    public bool SavePaymentMethod { get; set; } = false;
    public CustomerInfo? Customer { get; set; }
    public string? ExistingCustomerId { get; set; }
}

public class CustomerInfo
{
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
}

public class PaymentResponse
{
    public string ClientSecret { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string PaymentIntentId { get; set; } = string.Empty;
}

public class CustomerResponse
{
    public string CustomerId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public List<SavedPaymentMethod> SavedPaymentMethods { get; set; } = new();
}

public class SavedPaymentMethod
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Last4 { get; set; } = string.Empty;
    public string Brand { get; set; } = string.Empty;
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
}

public class SetupIntentRequest
{
    public string CustomerId { get; set; } = string.Empty;
}

public class SetupIntentResponse
{
    public string ClientSecret { get; set; } = string.Empty;
    public string SetupIntentId { get; set; } = string.Empty;
}