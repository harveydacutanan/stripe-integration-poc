using Azure;
using Azure.Data.Tables;
using POC.Models;

namespace POC.Services;

/// <summary>
/// Service for persisting webhook events to Azure Table Storage
/// </summary>
public class WebhookStorageService
{
    private readonly TableClient _tableClient;
    private readonly ILogger<WebhookStorageService> _logger;
    private const string TableName = "WebhookEvents";

    public WebhookStorageService(IConfiguration configuration, ILogger<WebhookStorageService> logger)
    {
        _logger = logger;

        var connectionString = configuration["AzureStorage:ConnectionString"];

        if (string.IsNullOrEmpty(connectionString))
        {
            _logger.LogWarning("Azure Storage connection string not configured. Webhook persistence disabled.");
            // Use development storage for local testing
            connectionString = "UseDevelopmentStorage=true";
        }

        var tableServiceClient = new TableServiceClient(connectionString);
        _tableClient = tableServiceClient.GetTableClient(TableName);

        // Create table if it doesn't exist
        try
        {
            _tableClient.CreateIfNotExists();
            _logger.LogInformation("Azure Table Storage initialized: {TableName}", TableName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Azure Table Storage");
        }
    }

    /// <summary>
    /// Stores a webhook event in Azure Table Storage
    /// </summary>
    public async Task<bool> StoreWebhookEventAsync(WebhookEventData eventData, WebhookProcessingResult result)
    {
        try
        {
            var entity = WebhookTableEntity.FromEventData(eventData);
            entity.Success = result.Success;
            entity.ProcessedAt = result.ProcessedAt;
            entity.ErrorMessage = result.ErrorDetails;
            entity.ProcessingNotes = result.Message;

            await _tableClient.AddEntityAsync(entity);

            _logger.LogInformation("Webhook event stored: {EventId} - {EventType}",
                eventData.EventId, eventData.EventType);

            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            // Entity already exists (duplicate event)
            _logger.LogWarning("Duplicate webhook event detected: {EventId}", eventData.EventId);
            await IncrementRetryCountAsync(eventData.EventType, eventData.EventId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to store webhook event: {EventId}", eventData.EventId);
            return false;
        }
    }

    /// <summary>
    /// Checks if a webhook event has already been processed (idempotency check)
    /// </summary>
    public async Task<bool> IsEventProcessedAsync(string eventId, string eventType)
    {
        try
        {
            var query = _tableClient.QueryAsync<WebhookTableEntity>(
                filter: $"EventId eq '{eventId}'",
                maxPerPage: 1
            );

            await foreach (var entity in query)
            {
                _logger.LogInformation("Webhook event already processed: {EventId}", eventId);
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if event is processed: {EventId}", eventId);
            // Fail open - allow processing if we can't check
            return false;
        }
    }

    /// <summary>
    /// Retrieves recent webhook events
    /// </summary>
    public async Task<List<WebhookTableEntity>> GetRecentEventsAsync(int limit = 50, string? eventType = null)
    {
        try
        {
            var events = new List<WebhookTableEntity>();

            string? filter = null;
            if (!string.IsNullOrEmpty(eventType))
            {
                filter = $"PartitionKey eq '{WebhookTableEntity.FromEventData(new WebhookEventData { EventType = eventType }).PartitionKey}'";
            }

            var query = _tableClient.QueryAsync<WebhookTableEntity>(
                filter: filter,
                maxPerPage: limit
            );

            await foreach (var entity in query)
            {
                events.Add(entity);
                if (events.Count >= limit) break;
            }

            _logger.LogInformation("Retrieved {Count} webhook events", events.Count);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve webhook events");
            return new List<WebhookTableEntity>();
        }
    }

    /// <summary>
    /// Gets webhook events for a specific customer
    /// </summary>
    public async Task<List<WebhookTableEntity>> GetEventsByCustomerAsync(string customerId, int limit = 50)
    {
        try
        {
            var events = new List<WebhookTableEntity>();

            var query = _tableClient.QueryAsync<WebhookTableEntity>(
                filter: $"CustomerId eq '{customerId}'",
                maxPerPage: limit
            );

            await foreach (var entity in query)
            {
                events.Add(entity);
                if (events.Count >= limit) break;
            }

            _logger.LogInformation("Retrieved {Count} webhook events for customer {CustomerId}",
                events.Count, customerId);

            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve webhook events for customer: {CustomerId}", customerId);
            return new List<WebhookTableEntity>();
        }
    }

    /// <summary>
    /// Gets failed webhook events for retry
    /// </summary>
    public async Task<List<WebhookTableEntity>> GetFailedEventsAsync(int limit = 50)
    {
        try
        {
            var events = new List<WebhookTableEntity>();

            var query = _tableClient.QueryAsync<WebhookTableEntity>(
                filter: "Success eq false",
                maxPerPage: limit
            );

            await foreach (var entity in query)
            {
                events.Add(entity);
                if (events.Count >= limit) break;
            }

            _logger.LogInformation("Retrieved {Count} failed webhook events", events.Count);
            return events;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve failed webhook events");
            return new List<WebhookTableEntity>();
        }
    }

    /// <summary>
    /// Updates a webhook event (for retry scenarios)
    /// </summary>
    public async Task<bool> UpdateWebhookEventAsync(WebhookTableEntity entity)
    {
        try
        {
            await _tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
            _logger.LogInformation("Webhook event updated: {EventId}", entity.EventId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update webhook event: {EventId}", entity.EventId);
            return false;
        }
    }

    /// <summary>
    /// Increments retry count for duplicate events
    /// </summary>
    private async Task IncrementRetryCountAsync(string eventType, string eventId)
    {
        try
        {
            var query = _tableClient.QueryAsync<WebhookTableEntity>(
                filter: $"EventId eq '{eventId}'",
                maxPerPage: 1
            );

            await foreach (var entity in query)
            {
                entity.RetryCount++;
                entity.ProcessingNotes = $"Retry attempt #{entity.RetryCount}";
                await _tableClient.UpdateEntityAsync(entity, entity.ETag, TableUpdateMode.Replace);
                break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to increment retry count for event: {EventId}", eventId);
        }
    }

    /// <summary>
    /// Gets webhook statistics
    /// </summary>
    public async Task<WebhookStatistics> GetStatisticsAsync(DateTime? since = null)
    {
        try
        {
            var stats = new WebhookStatistics();
            var cutoffDate = since ?? DateTime.UtcNow.AddDays(-7);

            var query = _tableClient.QueryAsync<WebhookTableEntity>(
                filter: $"ReceivedAt ge datetime'{cutoffDate:yyyy-MM-ddTHH:mm:ssZ}'"
            );

            await foreach (var entity in query)
            {
                stats.TotalEvents++;
                if (entity.Success) stats.SuccessfulEvents++;
                else stats.FailedEvents++;

                if (entity.RetryCount > 0) stats.RetryCount += entity.RetryCount;
            }

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get webhook statistics");
            return new WebhookStatistics();
        }
    }
}

/// <summary>
/// Webhook statistics model
/// </summary>
public class WebhookStatistics
{
    public int TotalEvents { get; set; }
    public int SuccessfulEvents { get; set; }
    public int FailedEvents { get; set; }
    public int RetryCount { get; set; }
    public double SuccessRate => TotalEvents > 0 ? (double)SuccessfulEvents / TotalEvents * 100 : 0;
}
