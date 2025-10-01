using Stripe;
using POC.Models;
using System.Text.Json;

namespace POC.Services;

public class WebhookService
{
    private readonly ILogger<WebhookService> _logger;
    private readonly IConfiguration _configuration;

    public WebhookService(ILogger<WebhookService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Processes incoming Stripe webhook events
    /// </summary>
    public async Task<WebhookProcessingResult> ProcessWebhookEventAsync(Event stripeEvent)
    {
        var result = new WebhookProcessingResult();

        try
        {
            _logger.LogInformation("Processing webhook event: {EventId} - {EventType}",
                stripeEvent.Id, stripeEvent.Type);

            // Extract event data
            var eventData = ExtractEventData(stripeEvent);

            // Process based on event type
            result = stripeEvent.Type switch
            {
                Events.PaymentIntentSucceeded => await HandlePaymentSucceeded(stripeEvent, eventData),
                Events.PaymentIntentPaymentFailed => await HandlePaymentFailed(stripeEvent, eventData),
                Events.PaymentIntentRequiresAction => await HandlePaymentRequiresAction(stripeEvent, eventData),
                Events.SetupIntentSucceeded => await HandleSetupIntentSucceeded(stripeEvent, eventData),
                Events.SetupIntentSetupFailed => await HandleSetupIntentFailed(stripeEvent, eventData),
                Events.CustomerCreated => await HandleCustomerCreated(stripeEvent, eventData),
                Events.CustomerUpdated => await HandleCustomerUpdated(stripeEvent, eventData),
                Events.CustomerDeleted => await HandleCustomerDeleted(stripeEvent, eventData),
                Events.PaymentMethodAttached => await HandlePaymentMethodAttached(stripeEvent, eventData),
                Events.PaymentMethodDetached => await HandlePaymentMethodDetached(stripeEvent, eventData),
                Events.InvoicePaymentSucceeded => await HandleInvoicePaymentSucceeded(stripeEvent, eventData),
                Events.InvoicePaymentFailed => await HandleInvoicePaymentFailed(stripeEvent, eventData),
                _ => await HandleUnknownEvent(stripeEvent, eventData)
            };

            if (result.Success)
            {
                _logger.LogInformation("Successfully processed webhook event: {EventId}", stripeEvent.Id);
            }
            else
            {
                _logger.LogWarning("Failed to process webhook event: {EventId} - {Error}",
                    stripeEvent.Id, result.ErrorDetails);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook event: {EventId}", stripeEvent.Id);

            return new WebhookProcessingResult
            {
                Success = false,
                Message = "Webhook processing failed",
                ErrorDetails = ex.Message
            };
        }
    }

    /// <summary>
    /// Validates webhook signature
    /// </summary>
    public bool ValidateWebhookSignature(string payload, string signature)
    {
        try
        {
            var webhookSecret = _configuration["Stripe:WebhookSecret"];
            if (string.IsNullOrEmpty(webhookSecret))
            {
                _logger.LogWarning("Webhook secret not configured");
                return false;
            }

            // Stripe.NET automatically validates the signature when constructing the event
            var stripeEvent = EventUtility.ConstructEvent(payload, signature, webhookSecret);
            return stripeEvent != null;
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Webhook signature validation failed");
            return false;
        }
    }

    #region Event Handlers

    private async Task<WebhookProcessingResult> HandlePaymentSucceeded(Event stripeEvent, WebhookEventData eventData)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;

        _logger.LogInformation("Payment succeeded: {PaymentIntentId} for amount {Amount} {Currency}",
            paymentIntent?.Id, paymentIntent?.Amount, paymentIntent?.Currency);

        // Here you would typically:
        // 1. Update your database with the successful payment
        // 2. Send confirmation emails
        // 3. Fulfill the order
        // 4. Update customer records

        // Simulate business logic
        await Task.Delay(100); // Replace with actual business logic

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Payment succeeded for {paymentIntent?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandlePaymentFailed(Event stripeEvent, WebhookEventData eventData)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;

        _logger.LogWarning("Payment failed: {PaymentIntentId} - {FailureReason}",
            paymentIntent?.Id, paymentIntent?.LastPaymentError?.Message);

        // Here you would typically:
        // 1. Update order status to failed
        // 2. Send failure notification
        // 3. Log for retry or manual review

        await Task.Delay(100); // Replace with actual business logic

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Payment failure processed for {paymentIntent?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandlePaymentRequiresAction(Event stripeEvent, WebhookEventData eventData)
    {
        var paymentIntent = stripeEvent.Data.Object as PaymentIntent;

        _logger.LogInformation("Payment requires action: {PaymentIntentId}",
            paymentIntent?.Id);

        // Here you would typically:
        // 1. Update payment status
        // 2. Potentially notify customer about required action

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Payment requiring action processed for {paymentIntent?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandleSetupIntentSucceeded(Event stripeEvent, WebhookEventData eventData)
    {
        var setupIntent = stripeEvent.Data.Object as SetupIntent;

        _logger.LogInformation("Setup intent succeeded: {SetupIntentId} for customer {CustomerId}",
            setupIntent?.Id, setupIntent?.CustomerId);

        // Here you would typically:
        // 1. Update customer record with new payment method
        // 2. Send confirmation that card was saved

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Setup intent succeeded for {setupIntent?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandleSetupIntentFailed(Event stripeEvent, WebhookEventData eventData)
    {
        var setupIntent = stripeEvent.Data.Object as SetupIntent;

        _logger.LogWarning("Setup intent failed: {SetupIntentId}",
            setupIntent?.Id);

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Setup intent failure processed for {setupIntent?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandleCustomerCreated(Event stripeEvent, WebhookEventData eventData)
    {
        var customer = stripeEvent.Data.Object as Customer;

        _logger.LogInformation("Customer created: {CustomerId} - {Email}",
            customer?.Id, customer?.Email);

        // Here you would typically:
        // 1. Update your local customer database
        // 2. Send welcome email
        // 3. Set up any default preferences

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Customer creation processed for {customer?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandleCustomerUpdated(Event stripeEvent, WebhookEventData eventData)
    {
        var customer = stripeEvent.Data.Object as Customer;

        _logger.LogInformation("Customer updated: {CustomerId}",
            customer?.Id);

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Customer update processed for {customer?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandleCustomerDeleted(Event stripeEvent, WebhookEventData eventData)
    {
        var customer = stripeEvent.Data.Object as Customer;

        _logger.LogInformation("Customer deleted: {CustomerId}",
            customer?.Id);

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Customer deletion processed for {customer?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandlePaymentMethodAttached(Event stripeEvent, WebhookEventData eventData)
    {
        var paymentMethod = stripeEvent.Data.Object as PaymentMethod;

        _logger.LogInformation("Payment method attached: {PaymentMethodId} to customer {CustomerId}",
            paymentMethod?.Id, paymentMethod?.CustomerId);

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Payment method attachment processed for {paymentMethod?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandlePaymentMethodDetached(Event stripeEvent, WebhookEventData eventData)
    {
        var paymentMethod = stripeEvent.Data.Object as PaymentMethod;

        _logger.LogInformation("Payment method detached: {PaymentMethodId}",
            paymentMethod?.Id);

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Payment method detachment processed for {paymentMethod?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandleInvoicePaymentSucceeded(Event stripeEvent, WebhookEventData eventData)
    {
        var invoice = stripeEvent.Data.Object as Invoice;

        _logger.LogInformation("Invoice payment succeeded: {InvoiceId}",
            invoice?.Id);

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Invoice payment success processed for {invoice?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandleInvoicePaymentFailed(Event stripeEvent, WebhookEventData eventData)
    {
        var invoice = stripeEvent.Data.Object as Invoice;

        _logger.LogWarning("Invoice payment failed: {InvoiceId}",
            invoice?.Id);

        await Task.Delay(100);

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Invoice payment failure processed for {invoice?.Id}"
        };
    }

    private async Task<WebhookProcessingResult> HandleUnknownEvent(Event stripeEvent, WebhookEventData eventData)
    {
        _logger.LogInformation("Received unknown webhook event type: {EventType}", stripeEvent.Type);

        await Task.CompletedTask;

        return new WebhookProcessingResult
        {
            Success = true,
            Message = $"Unknown event type {stripeEvent.Type} logged"
        };
    }

    #endregion

    private WebhookEventData ExtractEventData(Event stripeEvent)
    {
        var eventData = new WebhookEventData
        {
            EventId = stripeEvent.Id,
            EventType = stripeEvent.Type,
            CreatedAt = stripeEvent.Created,
            LiveMode = stripeEvent.Livemode,
            RawData = JsonSerializer.Serialize(stripeEvent.Data.Object)
        };

        // Extract common properties based on object type
        switch (stripeEvent.Data.Object)
        {
            case PaymentIntent pi:
                eventData.ObjectType = "payment_intent";
                eventData.ObjectId = pi.Id;
                eventData.PaymentIntentId = pi.Id;
                eventData.CustomerId = pi.CustomerId;
                eventData.Amount = pi.Amount;
                eventData.Currency = pi.Currency;
                eventData.Status = pi.Status;
                break;

            case SetupIntent si:
                eventData.ObjectType = "setup_intent";
                eventData.ObjectId = si.Id;
                eventData.SetupIntentId = si.Id;
                eventData.CustomerId = si.CustomerId;
                eventData.Status = si.Status;
                break;

            case Customer c:
                eventData.ObjectType = "customer";
                eventData.ObjectId = c.Id;
                eventData.CustomerId = c.Id;
                break;

            case PaymentMethod pm:
                eventData.ObjectType = "payment_method";
                eventData.ObjectId = pm.Id;
                eventData.PaymentMethodId = pm.Id;
                eventData.CustomerId = pm.CustomerId;
                break;

            case Invoice inv:
                eventData.ObjectType = "invoice";
                eventData.ObjectId = inv.Id;
                eventData.CustomerId = inv.CustomerId;
                eventData.Amount = inv.AmountPaid;
                eventData.Currency = inv.Currency;
                eventData.Status = inv.Status;
                break;
        }

        return eventData;
    }
}