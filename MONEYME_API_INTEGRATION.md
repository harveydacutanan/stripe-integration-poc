# MoneyMe.Payment.Api - Stripe Integration Guide

## Overview
This document outlines the integration of Stripe SDK into the existing MoneyMe.Payment.Api. All Stripe-related endpoints are grouped under the `api/stripe` prefix for better organization.

---

## API Endpoint Structure

### **Customer Management**
Base route: `api/stripe/customer`

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/stripe/customer/create` | Create new Stripe customer |
| GET | `/api/stripe/customer/{customerId}` | Get customer details with saved payment methods |
| GET | `/api/stripe/customer/by-email/{email}` | Find customer by email address |
| PUT | `/api/stripe/customer/{customerId}` | Update customer information |
| POST | `/api/stripe/customer/{customerId}/setup-intent` | Create setup intent to save payment method |
| GET | `/api/stripe/customer/{customerId}/payment-methods` | List all saved payment methods |
| DELETE | `/api/stripe/customer/payment-method/{paymentMethodId}` | Remove saved payment method |

---

### **Payment Processing**
Base route: `api/stripe/payment`

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/stripe/payment/create-intent` | Create payment intent (supports customer creation) |
| POST | `/api/stripe/payment/create-payment-intent` | Create payment intent (simplified for ECA) |
| POST | `/api/stripe/payment/create-intent-with-saved-method` | Charge using saved payment method |
| POST | `/api/stripe/payment/confirm-intent/{paymentIntentId}` | Confirm payment intent (3D Secure) |
| GET | `/api/stripe/payment/intent/{paymentIntentId}` | Get payment intent status |
| GET | `/api/stripe/payment/payment-method/{paymentMethodId}` | Get payment method details |
| GET | `/api/stripe/payment/saved-payment-methods/{customerId}` | Get saved payment methods for customer |
| DELETE | `/api/stripe/payment/payment-method/{paymentMethodId}` | Detach payment method |
| GET | `/api/stripe/payment/payment-history/{customerId}` | Get payment history with receipts |

---

### **Webhook Handling**
Base route: `api/stripe/webhook`

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/stripe/webhook/stripe` | **Main webhook endpoint (must be public)** |
| GET | `/api/stripe/webhook/test` | Test webhook configuration |
| GET | `/api/stripe/webhook/config` | Get webhook configuration info |
| POST | `/api/stripe/webhook/simulate/{eventType}` | Simulate webhook event (dev/test only) |

---

## Required NuGet Packages

```xml
<PackageReference Include="Stripe.net" Version="43.23.0" />
<PackageReference Include="Azure.Data.Tables" Version="12.11.0" />
```

---

## Configuration

### appsettings.json
```json
{
  "Stripe": {
    "SecretKey": "sk_test_...",
    "PublishableKey": "pk_test_...",
    "WebhookSecret": "whsec_..."
  }
}
```

### Program.cs Registration
```csharp
using Stripe;

var builder = WebApplication.CreateBuilder(args);

// Configure Stripe
StripeConfiguration.ApiKey = builder.Configuration["Stripe:SecretKey"];

// Register services
builder.Services.AddScoped<CustomerService>();
builder.Services.AddScoped<PaymentService>();
builder.Services.AddScoped<WebhookService>();
builder.Services.AddSingleton<WebhookStorageService>();
```

---

## Services to Integrate

### 1. CustomerService
**Purpose**: Manage Stripe customers and payment methods

**Key Methods:**
- `CreateCustomerAsync(CustomerRegistrationRequest)` - Create customer
- `FindCustomerByEmailAsync(string email)` - Find existing customer
- `GetCustomerWithPaymentMethodsAsync(string customerId)` - Get customer + saved cards
- `CreateSetupIntentAsync(string customerId)` - Setup intent for saving cards
- `GetCustomerPaymentMethodsAsync(string customerId)` - List saved payment methods
- `DetachPaymentMethodAsync(string paymentMethodId)` - Remove payment method
- `UpdateCustomerAsync(string customerId, CustomerRegistrationRequest)` - Update customer

### 2. PaymentService
**Purpose**: Process payments and manage payment intents

**Key Methods:**
- `CreateOneTimePaymentIntentAsync(decimal amount, string currency)` - One-time payment
- `CreateCustomerPaymentIntentAsync(string customerId, decimal amount, bool savePaymentMethod, string currency)` - Customer payment
- `CreatePaymentIntentWithSavedMethodAsync(string customerId, string paymentMethodId, decimal amount, string currency)` - Charge saved card
- `ConfirmPaymentIntentAsync(string paymentIntentId)` - Confirm payment
- `GetPaymentIntentAsync(string paymentIntentId)` - Get payment status
- `ProcessPaymentAsync(PaymentRequest)` - Complete payment flow

### 3. WebhookService
**Purpose**: Process Stripe webhook events

**Key Methods:**
- `ProcessWebhookEventAsync(Event stripeEvent)` - Main event processor
- `ValidateWebhookSignature(string payload, string signature)` - Signature validation

**Event Handlers:**
- `payment_intent.succeeded` - Payment completed
- `payment_intent.payment_failed` - Payment failed
- `payment_intent.requires_action` - 3D Secure required
- `setup_intent.succeeded` - Card saved successfully
- `setup_intent.setup_failed` - Failed to save card
- `customer.created/updated/deleted` - Customer lifecycle
- `payment_method.attached/detached` - Payment method changes
- `invoice.payment_succeeded/failed` - Invoice payments

### 4. WebhookStorageService
**Purpose**: Store webhook events and implement idempotency

**Key Methods:**
- `StoreWebhookEventAsync(WebhookEventData, WebhookProcessingResult)` - Store event
- `IsEventProcessedAsync(string eventId, string eventType)` - Check for duplicates

---

## Webhook Integration Details

### **Critical: Webhook Endpoint Must Be Public**

The webhook endpoint `POST /api/stripe/webhook/stripe` must be:
- Publicly accessible via HTTPS
- Registered in Stripe Dashboard
- Configured with webhook secret for signature validation

### **Webhook URL Examples**

**Development:**
```
https://localhost:5001/api/stripe/webhook/stripe
```
Use Stripe CLI for local testing:
```bash
stripe listen --forward-to https://localhost:5001/api/stripe/webhook/stripe
```

**Production:**
```
https://api.moneyme.com/api/stripe/webhook/stripe
```
Register this URL in Stripe Dashboard → Developers → Webhooks

### **Webhook Events to Subscribe**

In Stripe Dashboard, select these events:
- `payment_intent.succeeded`
- `payment_intent.payment_failed`
- `payment_intent.requires_action`
- `setup_intent.succeeded`
- `setup_intent.setup_failed`
- `customer.created`
- `customer.updated`
- `customer.deleted`
- `payment_method.attached`
- `payment_method.detached`
- `invoice.payment_succeeded`
- `invoice.payment_failed`

### **Webhook Security**

The webhook controller validates signatures using:
```csharp
var stripeEvent = EventUtility.ConstructEvent(requestBody, signature, webhookSecret);
```

**Never skip signature validation** - this prevents spoofed requests.

---

## Models/DTOs

### Request Models
```csharp
// Customer creation
public class CustomerRegistrationRequest
{
    public string Name { get; set; }
    public string Email { get; set; }
    public string? Phone { get; set; }
}

// Payment request
public class PaymentRequest
{
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "aud";
    public bool SavePaymentMethod { get; set; }
    public CustomerInfo? Customer { get; set; }
    public string? ExistingCustomerId { get; set; }
}

// Saved payment request
public class SavedPaymentRequest
{
    public string CustomerId { get; set; }
    public string PaymentMethodId { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "aud";
}

// Create payment intent request (ECA)
public class CreatePaymentIntentRequest
{
    public long Amount { get; set; }
    public string? Currency { get; set; }
    public string? CustomerId { get; set; }
    public string? PaymentMethodId { get; set; }
    public string? Description { get; set; }
    public bool SavePaymentMethod { get; set; }
}
```

### Response Models
```csharp
// Customer response
public class CustomerResponse
{
    public string CustomerId { get; set; }
    public string Name { get; set; }
    public string Email { get; set; }
    public string? Phone { get; set; }
    public List<SavedPaymentMethod> SavedPaymentMethods { get; set; }
}

// Payment response
public class PaymentResponse
{
    public string ClientSecret { get; set; }
    public string? CustomerId { get; set; }
    public string PaymentIntentId { get; set; }
}

// Setup intent response
public class SetupIntentResponse
{
    public string ClientSecret { get; set; }
    public string SetupIntentId { get; set; }
}

// Saved payment method
public class SavedPaymentMethod
{
    public string Id { get; set; }
    public string Type { get; set; }
    public string Last4 { get; set; }
    public string Brand { get; set; }
    public int ExpMonth { get; set; }
    public int ExpYear { get; set; }
}
```

---

## Payment Features

### **1. EFTPOS Support (Australian Debit)**
- Automatic network routing for lower fees
- Supports card and au_becs_debit payment types
- Stripe automatically selects best network (EFTPOS, Visa, Mastercard)

### **2. Off-Session Payments**
- Save payment methods for future charges
- Charge customers when not present
- Uses `SetupFutureUsage = "off_session"`

### **3. 3D Secure Authentication**
- Automatic 3DS when required
- Configured via `RequestThreeDSecure = "automatic"`
- Webhook handles `payment_intent.requires_action` event

### **4. Payment History**
- Retrieve all payments for a customer
- Include receipt URLs
- Show card details (last4, brand)

---

## Deployment Checklist

### Development Environment
- [ ] Install Stripe.NET NuGet package
- [ ] Configure test API keys in appsettings.Development.json
- [ ] Install Stripe CLI for local webhook testing
- [ ] Run `stripe login` to authenticate
- [ ] Forward webhooks: `stripe listen --forward-to https://localhost:5001/api/stripe/webhook/stripe`
- [ ] Update webhook secret from Stripe CLI output
- [ ] Test payment flows

### Production Environment
- [ ] Configure production API keys in Azure App Settings / Key Vault
- [ ] Deploy webhook endpoint to publicly accessible URL
- [ ] Register webhook endpoint in Stripe Dashboard
- [ ] Copy production webhook secret to app configuration
- [ ] Subscribe to required webhook events in Stripe Dashboard
- [ ] Test webhook delivery from Stripe Dashboard
- [ ] Set up monitoring/alerting for webhook failures
- [ ] Implement retry logic for failed webhooks
- [ ] Configure Azure Table Storage or database for event logging

---

## Error Handling

All endpoints implement:
- Try-catch blocks for Stripe exceptions
- Logging via ILogger
- Proper HTTP status codes (400, 404, 500)
- Structured error responses

Example:
```csharp
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
```

---

## Testing

### Test Cards (Stripe Test Mode)
```
Success: 4242 4242 4242 4242
3D Secure: 4000 0025 0000 3155
Declined: 4000 0000 0000 0002
Insufficient funds: 4000 0000 0000 9995
```

### Trigger Test Webhooks
```bash
stripe trigger payment_intent.succeeded
stripe trigger payment_intent.payment_failed
stripe trigger setup_intent.succeeded
stripe trigger customer.created
stripe trigger payment_method.attached
```

---

## Security Best Practices

1. **Never expose secret keys** - use secure configuration
2. **Always validate webhook signatures** - prevent spoofing
3. **Use HTTPS in production** - required for PCI compliance
4. **Separate test/production environments** - use different API keys
5. **Implement rate limiting** - protect webhook endpoint
6. **Log all transactions** - audit trail for compliance
7. **Never store card data** - use Stripe tokens/Elements only

---

## Support Resources

- Stripe API Documentation: https://stripe.com/docs/api
- Stripe.NET SDK: https://github.com/stripe/stripe-dotnet
- Webhook Testing: https://stripe.com/docs/webhooks/test
- EFTPOS Documentation: https://stripe.com/docs/payments/eftpos

---

## Summary

All Stripe integration endpoints are grouped under `api/stripe/*`:
- **Customer Management**: `/api/stripe/customer/*`
- **Payment Processing**: `/api/stripe/payment/*`
- **Webhook Handling**: `/api/stripe/webhook/*`

This organization makes it easy to:
- Apply route-level middleware (auth, rate limiting)
- Monitor Stripe-specific metrics
- Document and version Stripe endpoints separately
- Implement API gateway routing rules
