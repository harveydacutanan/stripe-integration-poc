namespace POC.Models;

public class WebhookEventData
{
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string ObjectType { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? PaymentIntentId { get; set; }
    public string? SetupIntentId { get; set; }
    public string? PaymentMethodId { get; set; }
    public long? Amount { get; set; }
    public string? Currency { get; set; }
    public string Status { get; set; } = string.Empty;
    public bool LiveMode { get; set; }
    public string RawData { get; set; } = string.Empty;
}

public class WebhookProcessingResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? ErrorDetails { get; set; }
    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}

public class WebhookLog
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string EventId { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ProcessingNotes { get; set; }
    public int RetryCount { get; set; } = 0;
}