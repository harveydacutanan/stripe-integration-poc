# Stripe Webhook Setup Guide

## Overview
This POC includes comprehensive webhook functionality to handle Stripe events. Webhooks allow your application to be notified when events happen in your Stripe account, such as successful payments, failed charges, or new customers.

## Webhook Events Supported

### Payment Events
- `payment_intent.succeeded` - Payment completed successfully
- `payment_intent.payment_failed` - Payment failed
- `payment_intent.requires_action` - Payment requires additional action (3D Secure)

### Setup Intent Events (Card Saving)
- `setup_intent.succeeded` - Card saved successfully
- `setup_intent.setup_failed` - Card saving failed

### Customer Events
- `customer.created` - New customer created
- `customer.updated` - Customer information updated
- `customer.deleted` - Customer deleted

### Payment Method Events
- `payment_method.attached` - Payment method added to customer
- `payment_method.detached` - Payment method removed from customer

### Invoice Events (for subscriptions)
- `invoice.payment_succeeded` - Invoice paid successfully
- `invoice.payment_failed` - Invoice payment failed

## Setup Instructions

### 1. Configure Webhook Secret

Add your Stripe webhook secret to `appsettings.json`:

```json
{
  "Stripe": {
    "PublishableKey": "pk_test_your_publishable_key",
    "SecretKey": "sk_test_your_secret_key",
    "WebhookSecret": "whsec_your_webhook_secret_here"
  }
}
```

### 2. Stripe Dashboard Configuration

1. Go to [Stripe Webhooks Dashboard](https://dashboard.stripe.com/webhooks)
2. Click **"Add endpoint"**
3. Enter your webhook URL: `https://your-domain.com/api/webhook/stripe`
4. Select events to listen for (or select all events)
5. Copy the **Signing Secret** and add it to your configuration

### 3. Development Setup with Stripe CLI

For local development, use the Stripe CLI to forward events:

```bash
# Install Stripe CLI
# https://stripe.com/docs/stripe-cli

# Login to your Stripe account
stripe login

# Forward events to your local endpoint
stripe listen --forward-to localhost:5000/api/webhook/stripe
```

The CLI will display a webhook signing secret - use this in your `appsettings.Development.json`.

### 4. Testing Your Webhook

#### Using the Built-in Test Page
Navigate to `/webhook-test.html` in your application to:
- Check webhook configuration
- Test endpoint connectivity
- Simulate webhook events
- View event logs

#### Using Stripe CLI
```bash
# Trigger a test payment event
stripe trigger payment_intent.succeeded

# Trigger a test setup intent event
stripe trigger setup_intent.succeeded

# Trigger a test customer creation event
stripe trigger customer.created
```

#### Manual Testing with curl
```bash
curl -X POST https://your-domain.com/api/webhook/test
```

## Webhook Endpoint Details

### Main Webhook Endpoint
- **URL**: `/api/webhook/stripe`
- **Method**: POST
- **Content-Type**: application/json
- **Headers**: `Stripe-Signature` (required)

### Additional Endpoints
- **GET** `/api/webhook/test` - Test endpoint health
- **GET** `/api/webhook/config` - Get configuration status
- **POST** `/api/webhook/simulate/{eventType}` - Simulate events for testing

## Security Features

### Signature Verification
All webhook requests are verified using Stripe's signature verification:
```csharp
var stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);
```

### Error Handling
- Invalid signatures return `401 Unauthorized`
- Processing errors return `500 Internal Server Error`
- All events are logged for debugging

### Retry Logic
Stripe automatically retries failed webhook deliveries with exponential backoff for up to 3 days.

## Business Logic Integration

Each event handler includes placeholder logic for common business operations:

```csharp
private async Task<WebhookProcessingResult> HandlePaymentSucceeded(Event stripeEvent, WebhookEventData eventData)
{
    var paymentIntent = stripeEvent.Data.Object as PaymentIntent;

    // Your business logic here:
    // 1. Update your database with the successful payment
    // 2. Send confirmation emails
    // 3. Fulfill the order
    // 4. Update customer records

    return new WebhookProcessingResult
    {
        Success = true,
        Message = $"Payment succeeded for {paymentIntent?.Id}"
    };
}
```

## Monitoring and Logging

### Built-in Logging
All webhook events are logged with structured logging:
```
[Information] Processing webhook event: evt_1234 - payment_intent.succeeded
[Information] Payment succeeded: pi_1234 for amount 2000 usd
[Information] Successfully processed webhook event: evt_1234
```

### Event Data Storage
The `WebhookEventData` model captures key information:
- Event ID and type
- Object details (payment intent, customer, etc.)
- Processing timestamps
- Success/failure status

## Production Considerations

### Idempotency
Implement idempotency to handle duplicate events:
```csharp
// Check if event was already processed
if (await IsEventProcessedAsync(stripeEvent.Id))
{
    return Ok("Event already processed");
}
```

### Database Integration
Store webhook events and processing results in your database for:
- Audit trails
- Retry mechanisms
- Business intelligence

### Scalability
Consider using a message queue for high-volume webhook processing:
- Azure Service Bus
- RabbitMQ
- AWS SQS

### Rate Limiting
Implement rate limiting to protect against webhook storms.

## Troubleshooting

### Common Issues

1. **Invalid Signature**
   - Verify webhook secret is correct
   - Ensure raw request body is used for signature verification
   - Check for encoding issues

2. **Timeout Errors**
   - Webhook processing should complete within 20 seconds
   - Use async operations for long-running tasks
   - Consider queuing for complex processing

3. **Event Not Received**
   - Check Stripe Dashboard for delivery attempts
   - Verify endpoint URL is accessible
   - Check firewall and security groups

### Debug Information
Access `/api/webhook/config` to verify:
- Webhook secret configuration
- Supported events
- Endpoint status

### Stripe Dashboard
Monitor webhook deliveries in the Stripe Dashboard:
- Delivery attempts and responses
- Failed deliveries and retry attempts
- Event payload details

## Testing Checklist

- [ ] Webhook secret configured
- [ ] Endpoint responds to test requests
- [ ] Signature verification working
- [ ] Payment success events processed
- [ ] Payment failure events processed
- [ ] Customer creation events processed
- [ ] Setup intent events processed
- [ ] Error handling working
- [ ] Logging and monitoring active