using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Scoped runner that processes pending outbound Slack/Discord webhook deliveries from
/// <c>webhook_delivery_outbox</c> with exponential backoff and circuit breaking (Phase 15.2 §T15.2.3).
/// <para>
/// Extracted from <see cref="WebhookOutboxDispatcher"/> so integration tests can invoke a delivery pass
/// deterministically without waiting on background timers.
/// </para>
/// </summary>
public sealed class WebhookOutboxRunner
{
    public const int MaxErrorLength = 500;

    private readonly TeamNexusDbContext _db;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IntegrationsOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<WebhookOutboxRunner> _logger;

    public WebhookOutboxRunner(
        TeamNexusDbContext db,
        IHttpClientFactory httpClientFactory,
        IOptions<IntegrationsOptions> options,
        TimeProvider clock,
        ILogger<WebhookOutboxRunner> logger)
    {
        _db = db;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Exponential backoff ladder after failed attempt <paramref name="attemptCount"/>:
    /// <c>5s → 30s → 2m → 10m → 30m</c>.
    /// </summary>
    public static TimeSpan GetRetryDelay(int attemptCount) => attemptCount switch
    {
        <= 1 => TimeSpan.FromSeconds(5),
        2 => TimeSpan.FromSeconds(30),
        3 => TimeSpan.FromMinutes(2),
        4 => TimeSpan.FromMinutes(10),
        _ => TimeSpan.FromMinutes(30),
    };

    /// <summary>
    /// Processes up to <c>Integrations:Outbox:BatchSize</c> due <c>Pending</c> deliveries.
    /// Returns the number of outbox rows processed in this pass.
    /// </summary>
    public async Task<int> ProcessBatchAsync(DateTimeOffset? now = null, CancellationToken ct = default)
    {
        var utcNow = now ?? _clock.GetUtcNow();
        var batchSize = _options.Outbox.EffectiveBatchSize;

        var dueItems = await _db.WebhookDeliveryOutbox
            .Include(o => o.Endpoint)
            .Where(o => o.Status == "Pending" && o.NextAttemptAt <= utcNow)
            .OrderBy(o => o.NextAttemptAt)
            .ThenBy(o => o.CreatedAt)
            .Take(batchSize)
            .ToListAsync(ct);

        if (dueItems.Count == 0)
        {
            return 0;
        }

        foreach (var item in dueItems)
        {
            await DeliverCoreAsync(item, utcNow, allowInactiveEndpoint: false, ct);
        }

        await _db.SaveChangesAsync(ct);
        return dueItems.Count;
    }

    /// <summary>
    /// Delivers a single outbox row immediately (used by synchronous <c>POST /test</c> verification).
    /// </summary>
    public async Task<WebhookDeliveryOutbox> DeliverSingleAsync(
        Guid outboxId,
        DateTimeOffset? now = null,
        CancellationToken ct = default)
    {
        var utcNow = now ?? _clock.GetUtcNow();

        var item = await _db.WebhookDeliveryOutbox
            .Include(o => o.Endpoint)
            .FirstOrDefaultAsync(o => o.Id == outboxId, ct)
            ?? throw new InvalidOperationException($"Outbox delivery '{outboxId}' was not found.");

        await DeliverCoreAsync(item, utcNow, allowInactiveEndpoint: true, ct);
        await _db.SaveChangesAsync(ct);

        return item;
    }

    private async Task DeliverCoreAsync(
        WebhookDeliveryOutbox item,
        DateTimeOffset utcNow,
        bool allowInactiveEndpoint,
        CancellationToken ct)
    {
        var endpoint = item.Endpoint;
        if (endpoint is null)
        {
            item.Status = "Failed";
            item.LastError = "Không tìm thấy cấu hình Webhook Endpoint.";
            return;
        }

        if (!allowInactiveEndpoint && !endpoint.IsActive)
        {
            item.Status = "Failed";
            item.LastError = "Webhook Endpoint đã tạm ngắt do vượt ngưỡng lỗi hoặc bị tắt bởi Quản lý.";
            return;
        }

        try
        {
            // Defense-in-depth: re-verify SSRF rules right before dispatching the HTTP request.
            var (_, validatedUrl) = WebhookUrlValidator.Validate(endpoint.Provider, endpoint.TargetUrl);

            var client = _httpClientFactory.CreateClient(AiModule.ExternalWebhookHttpClientName);
            using var request = new HttpRequestMessage(HttpMethod.Post, validatedUrl)
            {
                Content = new StringContent(item.PayloadJson, Encoding.UTF8, "application/json"),
            };

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            var statusCode = (int)response.StatusCode;

            if (statusCode is >= 200 and <= 299)
            {
                item.AttemptCount++;
                item.Status = "Sent";
                item.SentAt = utcNow;
                item.LastHttpStatus = statusCode;
                item.LastError = null;

                endpoint.FailureCount = 0;
                endpoint.LastSuccessAt = utcNow;
                endpoint.LastError = null;
                return;
            }

            RecordFailure(
                item,
                endpoint,
                utcNow,
                statusCode,
                $"Máy chủ đích ({endpoint.Provider}) phản hồi mã lỗi HTTP {statusCode}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            var safeMessage = SanitizeError(ex.Message, endpoint.TargetUrl);
            RecordFailure(
                item,
                endpoint,
                utcNow,
                httpStatus: null,
                $"Lỗi kết nối tới Webhook ({endpoint.Provider}): {safeMessage}");
        }
    }

    private void RecordFailure(
        WebhookDeliveryOutbox item,
        WorkspaceWebhookEndpoint endpoint,
        DateTimeOffset utcNow,
        int? httpStatus,
        string rawError)
    {
        var sanitized = SanitizeError(rawError, endpoint.TargetUrl);

        item.AttemptCount++;
        item.LastHttpStatus = httpStatus;
        item.LastError = sanitized;

        endpoint.FailureCount++;
        endpoint.LastFailureAt = utcNow;
        endpoint.LastError = sanitized;

        var maxAttempts = item.MaxAttempts > 0 ? item.MaxAttempts : _options.Outbox.EffectiveMaxAttempts;
        if (item.AttemptCount >= maxAttempts)
        {
            item.Status = "Failed";
        }
        else
        {
            item.Status = "Pending";
            item.NextAttemptAt = utcNow.Add(GetRetryDelay(item.AttemptCount));
        }

        var threshold = _options.Outbox.EffectiveCircuitBreakerThreshold;
        if (endpoint.FailureCount >= threshold && endpoint.IsActive)
        {
            endpoint.IsActive = false;
            _logger.LogWarning(
                "Circuit breaker triggered for webhook endpoint {EndpointId} in workspace {WorkspaceId} "
                + "after {FailureCount} consecutive failures.",
                endpoint.Id,
                endpoint.WorkspaceId,
                endpoint.FailureCount);
        }
    }

    /// <summary>
    /// Ensures the secret webhook URL never leaks into persisted error messages or logs.
    /// </summary>
    public static string SanitizeError(string? error, string? secretUrl)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "Lỗi không xác định khi gửi Webhook.";
        }

        var text = error.Trim();
        if (!string.IsNullOrWhiteSpace(secretUrl))
        {
            text = text.Replace(secretUrl.Trim(), WebhookUrlValidator.MaskUrl(secretUrl), StringComparison.OrdinalIgnoreCase);

            if (Uri.TryCreate(secretUrl.Trim(), UriKind.Absolute, out var uri))
            {
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length > 0)
                {
                    var lastSegment = segments[^1];
                    if (lastSegment.Length >= 6)
                    {
                        text = text.Replace(lastSegment, "****", StringComparison.OrdinalIgnoreCase);
                    }
                }
            }
        }

        return text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
    }
}

