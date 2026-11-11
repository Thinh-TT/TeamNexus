using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Transactional outbox implementation of <see cref="IExternalAlertSink"/> (Phase 15.2 §T15.2.3).
/// <para>
/// Replaces <see cref="NoOpExternalAlertSink"/>: whenever <c>NotificationService</c> persists an in-app
/// alert, this sink inspects the workspace's active <c>workspace_webhook_endpoints</c>, formats the
/// provider-specific payload (Slack Block Kit or Discord Embeds), and enqueues a <c>Pending</c> row in
/// <c>webhook_delivery_outbox</c>.
/// </para>
/// <para>
/// <b>Graceful degradation &amp; never throws:</b> If the workspace has no active webhook endpoints, this
/// sink exits immediately without writing to the database. Any unexpected error is logged and swallowed so
/// the primary business operation is never affected.
/// </para>
/// </summary>
public sealed class WebhookOutboxExternalAlertSink : IExternalAlertSink
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly IntegrationsOptions _options;
    private readonly ILogger<WebhookOutboxExternalAlertSink> _logger;

    public WebhookOutboxExternalAlertSink(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        IOptions<IntegrationsOptions> options,
        ILogger<WebhookOutboxExternalAlertSink> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(ExternalAlertMessage message, CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<TeamNexusDbContext>();
            var clock = scope.ServiceProvider.GetService<TimeProvider>() ?? TimeProvider.System;

            var activeEndpoints = await db.WorkspaceWebhookEndpoints
                .AsNoTracking()
                .Where(e => e.WorkspaceId == message.WorkspaceId && e.IsActive)
                .OrderBy(e => e.CreatedAt)
                .ToListAsync(ct);

            if (activeEndpoints.Count == 0)
            {
                return;
            }

            var matchedEndpoints = activeEndpoints
                .Where(e => WebhookEventMatcher.IsSubscribed(e.SubscribedEvents, message.Type))
                .ToList();

            if (matchedEndpoints.Count == 0)
            {
                return;
            }

            HashSet<Guid> alreadyEnqueuedEndpointIds = [];
            if (!string.IsNullOrWhiteSpace(message.DedupeKey))
            {
                var matchedIds = matchedEndpoints.Select(e => e.Id).ToList();
                var existing = await db.WebhookDeliveryOutbox
                    .AsNoTracking()
                    .Where(o => matchedIds.Contains(o.EndpointId) && o.DedupeKey == message.DedupeKey)
                    .Select(o => o.EndpointId)
                    .ToListAsync(ct);

                alreadyEnqueuedEndpointIds = existing.ToHashSet();
            }

            var utcNow = clock.GetUtcNow();
            var frontendBaseUrl = _configuration["Frontend:BaseUrl"];
            var maxAttempts = _options.Outbox.EffectiveMaxAttempts;
            var toAdd = new List<WebhookDeliveryOutbox>();

            foreach (var endpoint in matchedEndpoints)
            {
                if (alreadyEnqueuedEndpointIds.Contains(endpoint.Id))
                {
                    continue;
                }

                var payloadJson = string.Equals(endpoint.Provider, WebhookUrlValidator.ProviderDiscord, StringComparison.OrdinalIgnoreCase)
                    ? DiscordEmbedFormatter.Format(message, frontendBaseUrl, utcNow)
                    : SlackBlockKitFormatter.Format(message, frontendBaseUrl);

                toAdd.Add(new WebhookDeliveryOutbox
                {
                    EndpointId = endpoint.Id,
                    WorkspaceId = message.WorkspaceId,
                    NotificationId = message.NotificationId,
                    EventType = message.Type,
                    DedupeKey = message.DedupeKey,
                    PayloadJson = payloadJson,
                    Status = "Pending",
                    AttemptCount = 0,
                    MaxAttempts = maxAttempts,
                    NextAttemptAt = utcNow,
                });
            }

            if (toAdd.Count == 0)
            {
                return;
            }

            db.WebhookDeliveryOutbox.AddRange(toAdd);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "WebhookOutboxExternalAlertSink failed to enqueue {EventType} for workspace {WorkspaceId}; "
                + "in-app notification remains unaffected.",
                message.Type,
                message.WorkspaceId);
        }
    }
}

