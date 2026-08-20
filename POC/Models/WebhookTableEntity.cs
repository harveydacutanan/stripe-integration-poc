using Azure;
using Azure.Data.Tables;

namespace POC.Models;

/// <summary>
/// Azure Table Storage entity for storing webhook events
/// PartitionKey: EventType (e.g., "payment_intent.succeeded")
/// RowKey: EventId-Timestamp (for ordering and uniqueness)
/// </summary>
public class WebhookTableEntity : ITableEntity
{
    public string PartitionKey { get; set; } = string.Empty;
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    // Webhook Event Properties
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    // Object Properties
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? SetupIntentId { get; set; }
    public string? PaymentMethodId { get; set; }

    // Payment Details
    public long? Amount { get; set; }
    public string? Currency { get; set; }
    public string Status { get; set; } = string.Empty;

    // Processing Information
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProcessingNotes { get; set; }
    public int RetryCount { get; set; } = 0;

    // Metadata
    public bool LiveMode { get; set; }
    public string RawEventData { get; set; } = string.Empty;

    /// <summary>
    /// Creates a WebhookTableEntity from WebhookEventData
    /// </summary>
    public static WebhookTableEntity FromEventData(WebhookEventData eventData)
    {
        var entity = new WebhookTableEntity
        {
            // PartitionKey: EventType for efficient queries by event type
            PartitionKey = SanitizeKey(eventData.EventType),

            // RowKey: Reverse timestamp + EventId for chronological ordering (newest first)
            RowKey = $"{DateTime.MaxValue.Ticks - eventData.CreatedAt.Ticks:D19}_{eventData.EventId}",

            EventId = eventData.EventId,
            EventType = eventData.EventType,
            CreatedAt = eventData.CreatedAt,
            ReceivedAt = DateTime.UtcNow,
            ObjectType = eventData.ObjectType,
            ObjectId = eventData.ObjectId,
            CustomerId = eventData.CustomerId,
            PaymentIntentId = eventData.PaymentIntentId,
            SetupIntentId = eventData.SetupIntentId,
            PaymentMethodId = eventData.PaymentMethodId,
            Amount = eventData.Amount,
            Currency = eventData.Currency,
            Status = eventData.Status,
            LiveMode = eventData.LiveMode,
            RawEventData = eventData.RawData,
            Success = true,
            RetryCount = 0
        };

        return entity;
    }

    /// <summary>
    /// Sanitizes keys to comply with Azure Table Storage requirements
    /// </summary>
    private static string SanitizeKey(string key)
    {
        // Replace characters not allowed in Azure Table Storage keys
        return key.Replace("/", "_")
                  .Replace("\\", "_")
                  .Replace("#", "_")
                  .Replace("?", "_");
    }

    /// <summary>
    /// Converts to WebhookEventData
    /// </summary>
    public WebhookEventData ToEventData()
    {
        return new WebhookEventData
        {
            EventId = EventId,
            EventType = EventType,
            CreatedAt = CreatedAt,
            ObjectType = ObjectType,
            ObjectId = ObjectId,
            CustomerId = CustomerId,
            PaymentIntentId = PaymentIntentId,
            SetupIntentId = SetupIntentId,
            PaymentMethodId = PaymentMethodId,
            Amount = Amount,
            Currency = Currency,
            Status = Status,
            LiveMode = LiveMode,
            RawData = RawEventData
        };
    }
}
