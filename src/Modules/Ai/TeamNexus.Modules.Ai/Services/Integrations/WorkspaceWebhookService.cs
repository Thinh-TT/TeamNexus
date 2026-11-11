using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.DTOs.Integrations;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Integrations;

public sealed class WorkspaceWebhookService : IWorkspaceWebhookService
{
    public const int MaxNameLength = 120;
    public const int MaxDeliveryHistoryTake = 20;

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly WebhookOutboxRunner _outboxRunner;
    private readonly IConfiguration _configuration;
    private readonly IntegrationsOptions _options;
    private readonly TimeProvider _clock;

    public WorkspaceWebhookService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        WebhookOutboxRunner outboxRunner,
        IConfiguration configuration,
        IOptions<IntegrationsOptions> options,
        TimeProvider clock)
    {
        _db = db;
        _access = access;
        _outboxRunner = outboxRunner;
        _configuration = configuration;
        _options = options.Value;
        _clock = clock;
    }

    public async Task<WorkspaceWebhookListResponse> ListAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var endpoints = await _db.WorkspaceWebhookEndpoints
            .AsNoTracking()
            .Where(e => e.WorkspaceId == workspaceId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);

        var windowStart = _clock.GetUtcNow().AddHours(-24);

        var sentLast24h = await _db.WebhookDeliveryOutbox
            .AsNoTracking()
            .CountAsync(
                o => o.WorkspaceId == workspaceId
                     && o.Status == "Sent"
                     && o.SentAt >= windowStart,
                ct);

        var failedLast24h = await _db.WebhookDeliveryOutbox
            .AsNoTracking()
            .CountAsync(
                o => o.WorkspaceId == workspaceId
                     && o.Status == "Failed"
                     && o.UpdatedAt >= windowStart,
                ct);

        var pendingCount = await _db.WebhookDeliveryOutbox
            .AsNoTracking()
            .CountAsync(
                o => o.WorkspaceId == workspaceId
                     && o.Status == "Pending",
                ct);

        return new WorkspaceWebhookListResponse(
            new WorkspaceWebhookStatsResponse(sentLast24h, failedLast24h, pendingCount),
            endpoints.Select(ToEndpointResponse).ToList());
    }

    public async Task<WorkspaceWebhookEndpointResponse> CreateAsync(
        Guid workspaceId,
        CreateWorkspaceWebhookRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var name = ValidateName(request.Name);
        var (provider, validatedUrl) = WebhookUrlValidator.Validate(request.Provider, request.TargetUrl);
        var subscribedEventsCsv = WebhookEventMatcher.NormalizeAndValidateEvents(request.SubscribedEvents);

        var maxEndpoints = _options.EffectiveMaxEndpointsPerWorkspace;
        var currentCount = await _db.WorkspaceWebhookEndpoints
            .CountAsync(e => e.WorkspaceId == workspaceId, ct);

        if (currentCount >= maxEndpoints)
        {
            throw new BadRequestException(
                $"Mỗi không gian làm việc chỉ được cấu hình tối đa {maxEndpoints} Webhook Endpoint.");
        }

        var endpoint = new WorkspaceWebhookEndpoint
        {
            WorkspaceId = workspaceId,
            Name = name,
            Provider = provider,
            TargetUrl = validatedUrl,
            SubscribedEvents = subscribedEventsCsv,
            IsActive = true,
            FailureCount = 0,
            CreatedByUserId = userId,
        };

        _db.WorkspaceWebhookEndpoints.Add(endpoint);
        await _db.SaveChangesAsync(ct);

        return ToEndpointResponse(endpoint);
    }

    public async Task<WorkspaceWebhookEndpointResponse> UpdateAsync(
        Guid workspaceId,
        Guid endpointId,
        UpdateWorkspaceWebhookRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var endpoint = await _db.WorkspaceWebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.WorkspaceId == workspaceId, ct)
            ?? throw new NotFoundException("Không tìm thấy cấu hình Webhook Endpoint.");

        if (request.Name is not null)
        {
            endpoint.Name = ValidateName(request.Name);
        }

        if (request.TargetUrl is not null)
        {
            var (_, validatedUrl) = WebhookUrlValidator.Validate(endpoint.Provider, request.TargetUrl);
            endpoint.TargetUrl = validatedUrl;
        }

        if (request.SubscribedEvents is not null)
        {
            endpoint.SubscribedEvents = WebhookEventMatcher.NormalizeAndValidateEvents(request.SubscribedEvents);
        }

        if (request.IsActive.HasValue)
        {
            endpoint.IsActive = request.IsActive.Value;
            if (request.IsActive.Value)
            {
                endpoint.FailureCount = 0;
                endpoint.LastError = null;
            }
        }

        await _db.SaveChangesAsync(ct);
        return ToEndpointResponse(endpoint);
    }

    public async Task DeleteAsync(
        Guid workspaceId,
        Guid endpointId,
        Guid userId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var endpoint = await _db.WorkspaceWebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.WorkspaceId == workspaceId, ct)
            ?? throw new NotFoundException("Không tìm thấy cấu hình Webhook Endpoint.");

        _db.WorkspaceWebhookEndpoints.Remove(endpoint);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<WebhookTestResultResponse> SendTestAsync(
        Guid workspaceId,
        Guid endpointId,
        Guid userId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var endpoint = await _db.WorkspaceWebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId && e.WorkspaceId == workspaceId, ct)
            ?? throw new NotFoundException("Không tìm thấy cấu hình Webhook Endpoint.");

        var utcNow = _clock.GetUtcNow();
        var frontendBaseUrl = _configuration["Frontend:BaseUrl"];

        var testMessage = new ExternalAlertMessage(
            workspaceId,
            WebhookEventMatcher.TestPing,
            "✅ Kết nối TeamNexus Webhook thành công",
            $"Kênh '{endpoint.Name}' ({endpoint.Provider.ToUpperInvariant()}) đã sẵn sàng nhận thông báo tự động từ không gian làm việc của bạn.",
            PayloadJson: null,
            RecipientUserIds: [userId]);

        var payloadJson = string.Equals(endpoint.Provider, WebhookUrlValidator.ProviderDiscord, StringComparison.OrdinalIgnoreCase)
            ? DiscordEmbedFormatter.Format(testMessage, frontendBaseUrl, utcNow)
            : SlackBlockKitFormatter.Format(testMessage, frontendBaseUrl);

        var outbox = new WebhookDeliveryOutbox
        {
            EndpointId = endpoint.Id,
            WorkspaceId = workspaceId,
            NotificationId = null,
            EventType = WebhookEventMatcher.TestPing,
            DedupeKey = $"TestPing:{endpoint.Id}:{Guid.NewGuid():N}",
            PayloadJson = payloadJson,
            Status = "Pending",
            AttemptCount = 0,
            MaxAttempts = 1,
            NextAttemptAt = utcNow,
        };

        _db.WebhookDeliveryOutbox.Add(outbox);
        await _db.SaveChangesAsync(ct);

        var delivered = await _outboxRunner.DeliverSingleAsync(outbox.Id, utcNow, ct);
        var success = string.Equals(delivered.Status, "Sent", StringComparison.Ordinal);

        return new WebhookTestResultResponse(
            Success: success,
            DeliveryId: delivered.Id,
            Status: delivered.Status,
            HttpStatus: delivered.LastHttpStatus,
            Error: delivered.LastError,
            TestedAt: utcNow);
    }

    public async Task<IReadOnlyList<WebhookDeliveryItemResponse>> GetDeliveriesAsync(
        Guid workspaceId,
        Guid endpointId,
        Guid userId,
        CancellationToken ct = default)
    {
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var exists = await _db.WorkspaceWebhookEndpoints
            .AsNoTracking()
            .AnyAsync(e => e.Id == endpointId && e.WorkspaceId == workspaceId, ct);

        if (!exists)
        {
            throw new NotFoundException("Không tìm thấy cấu hình Webhook Endpoint.");
        }

        var items = await _db.WebhookDeliveryOutbox
            .AsNoTracking()
            .Where(o => o.EndpointId == endpointId && o.WorkspaceId == workspaceId)
            .OrderByDescending(o => o.CreatedAt)
            .Take(MaxDeliveryHistoryTake)
            .ToListAsync(ct);

        return items.Select(ToDeliveryResponse).ToList();
    }

    private static string ValidateName(string? name)
    {
        var trimmed = name?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new BadRequestException("Tên kênh Webhook không được để trống.");
        }

        if (trimmed.Length > MaxNameLength)
        {
            throw new BadRequestException($"Tên kênh Webhook không được vượt quá {MaxNameLength} ký tự.");
        }

        return trimmed;
    }

    private static WorkspaceWebhookEndpointResponse ToEndpointResponse(WorkspaceWebhookEndpoint endpoint)
        => new(
            endpoint.Id,
            endpoint.WorkspaceId,
            endpoint.Name,
            endpoint.Provider,
            WebhookUrlValidator.MaskUrl(endpoint.TargetUrl),
            WebhookEventMatcher.ParseCsv(endpoint.SubscribedEvents),
            endpoint.SubscribedEvents,
            endpoint.IsActive,
            endpoint.FailureCount,
            endpoint.LastSuccessAt,
            endpoint.LastFailureAt,
            endpoint.LastError,
            endpoint.CreatedByUserId,
            endpoint.CreatedAt,
            endpoint.UpdatedAt);

    private static WebhookDeliveryItemResponse ToDeliveryResponse(WebhookDeliveryOutbox item)
        => new(
            item.Id,
            item.EndpointId,
            item.EventType,
            item.Status,
            item.AttemptCount,
            item.MaxAttempts,
            item.LastHttpStatus,
            item.LastError,
            item.NextAttemptAt,
            item.SentAt,
            item.CreatedAt);
}

