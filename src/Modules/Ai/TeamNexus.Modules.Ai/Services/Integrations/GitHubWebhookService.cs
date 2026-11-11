using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Implementation of <see cref="IGitHubWebhookService"/> (Phase 15.3 §T15.3.3).
/// Handles inbound GitHub webhook events, maintains idempotency, manages <see cref="TaskExternalLink"/>
/// state machine, and moves tasks with accountability via <see cref="AiActionLog"/>.
/// </summary>
public sealed class GitHubWebhookService : IGitHubWebhookService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly ITaskAutomation _taskAutomation;
    private readonly TaskKeyParser _taskKeyParser;
    private readonly ILogger<GitHubWebhookService> _logger;

    public GitHubWebhookService(
        TeamNexusDbContext db,
        ITaskAutomation taskAutomation,
        TaskKeyParser taskKeyParser,
        ILogger<GitHubWebhookService> logger)
    {
        _db = db;
        _taskAutomation = taskAutomation;
        _taskKeyParser = taskKeyParser;
        _logger = logger;
    }

    public async Task<GitHubWebhookProcessResult> ProcessWebhookAsync(
        WorkspaceGitHubIntegration integration,
        string eventName,
        string deliveryId,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(integration);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);

        var isDuplicate = await _db.GitHubWebhookDeliveries
            .AnyAsync(d => d.IntegrationId == integration.Id && d.DeliveryId == deliveryId, ct);

        if (isDuplicate)
        {
            _logger.LogInformation(
                "Duplicate GitHub webhook delivery ignored (IntegrationId {IntegrationId}, DeliveryId {DeliveryId}).",
                integration.Id,
                deliveryId);

            return new GitHubWebhookProcessResult("Processed", IsDuplicate: true, Summary: "Giao vận trùng lặp đã được bỏ qua.");
        }

        if (string.Equals(eventName, "ping", StringComparison.OrdinalIgnoreCase))
        {
            return await HandlePingEventAsync(integration, deliveryId, ct);
        }

        if (string.Equals(eventName, "pull_request", StringComparison.OrdinalIgnoreCase))
        {
            return await HandlePullRequestEventAsync(integration, deliveryId, rawBody, ct);
        }

        return await HandleUnprocessedEventAsync(integration, eventName, deliveryId, ct);
    }

    private async Task<GitHubWebhookProcessResult> HandlePingEventAsync(
        WorkspaceGitHubIntegration integration,
        string deliveryId,
        CancellationToken ct)
    {
        var delivery = new GitHubWebhookDelivery
        {
            IntegrationId = integration.Id,
            WorkspaceId = integration.WorkspaceId,
            DeliveryId = deliveryId,
            EventName = "ping",
            Status = "Processed",
            Summary = "GitHub ping thành công.",
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        _db.GitHubWebhookDeliveries.Add(delivery);
        await SaveDeliverySafelyAsync(integration.Id, deliveryId, ct);

        return new GitHubWebhookProcessResult("Processed", Summary: delivery.Summary);
    }

    private async Task<GitHubWebhookProcessResult> HandleUnprocessedEventAsync(
        WorkspaceGitHubIntegration integration,
        string eventName,
        string deliveryId,
        CancellationToken ct)
    {
        var delivery = new GitHubWebhookDelivery
        {
            IntegrationId = integration.Id,
            WorkspaceId = integration.WorkspaceId,
            DeliveryId = deliveryId,
            EventName = eventName,
            Status = "Ignored",
            Summary = $"Sự kiện '{eventName}' chưa được xử lý trong giai đoạn 15.3.",
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        _db.GitHubWebhookDeliveries.Add(delivery);
        await SaveDeliverySafelyAsync(integration.Id, deliveryId, ct);

        return new GitHubWebhookProcessResult("Ignored", Summary: delivery.Summary);
    }

    private async Task<GitHubWebhookProcessResult> HandlePullRequestEventAsync(
        WorkspaceGitHubIntegration integration,
        string deliveryId,
        ReadOnlyMemory<byte> rawBody,
        CancellationToken ct)
    {
        using var document = JsonDocument.Parse(rawBody);
        var root = document.RootElement;

        var action = root.TryGetProperty("action", out var a) ? a.GetString() : null;
        var repoFullName = root.TryGetProperty("repository", out var repo)
                           && repo.TryGetProperty("full_name", out var fn)
            ? fn.GetString()
            : null;

        if (!root.TryGetProperty("pull_request", out var pr))
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "pull_request",
                action,
                repoFullName,
                "Ignored",
                "Payload pull_request không chứa thông tin PR.",
                ct);
        }

        var prNumber = pr.TryGetProperty("number", out var pn) ? pn.GetInt64() : 0L;
        var prTitle = pr.TryGetProperty("title", out var pt) ? pt.GetString() ?? string.Empty : string.Empty;
        var prHtmlUrl = pr.TryGetProperty("html_url", out var ph) ? ph.GetString() ?? string.Empty : string.Empty;
        var prBody = pr.TryGetProperty("body", out var pb) ? pb.GetString() : null;
        var isDraft = pr.TryGetProperty("draft", out var pd) && pd.GetBoolean();
        var isMerged = pr.TryGetProperty("merged", out var pm) && pm.GetBoolean();
        var headRef = pr.TryGetProperty("head", out var head)
                      && head.TryGetProperty("ref", out var hr)
            ? hr.GetString()
            : null;

        var prState = action switch
        {
            "closed" when isMerged => "merged",
            "closed" => "closed",
            _ when isDraft => "draft",
            _ => "open",
        };

        var taskNumbers = _taskKeyParser.ParseTaskNumbers([prTitle, headRef, prBody], maxCount: 5);

        if (taskNumbers.Count == 0)
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "pull_request",
                action,
                repoFullName,
                "Ignored",
                "Không tìm thấy mã thẻ (TN-*) trong Pull Request.",
                ct);
        }

        var tasks = await _db.Tasks
            .Include(t => t.Board)
            .Include(t => t.Column)
            .Where(t => t.Board != null
                        && t.Board.WorkspaceId == integration.WorkspaceId
                        && taskNumbers.Contains(t.TaskNumber))
            .ToListAsync(ct);

        if (tasks.Count == 0)
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "pull_request",
                action,
                repoFullName,
                "Ignored",
                $"Không tìm thấy thẻ nào khớp với mã [{string.Join(", ", taskNumbers)}] trong workspace.",
                ct);
        }

        var externalId = $"{repoFullName}#{prNumber}";
        var now = DateTimeOffset.UtcNow;

        foreach (var task in tasks)
        {
            var link = await _db.TaskExternalLinks
                .FirstOrDefaultAsync(l => l.TaskId == task.Id
                                          && l.Provider == "github"
                                          && l.LinkType == "pull_request"
                                          && l.ExternalId == externalId, ct);

            if (link is null)
            {
                link = new TaskExternalLink
                {
                    WorkspaceId = integration.WorkspaceId,
                    TaskId = task.Id,
                    Provider = "github",
                    LinkType = "pull_request",
                    ExternalId = externalId,
                    ExternalUrl = prHtmlUrl,
                    Title = prTitle,
                    State = prState,
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.TaskExternalLinks.Add(link);
            }
            else
            {
                link.Title = prTitle;
                link.ExternalUrl = prHtmlUrl;
                link.State = prState;
                link.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(ct);

            if (integration.AutoMoveOnPr)
            {
                await ApplyPrStateMachineAsync(integration, task, prNumber, repoFullName, prTitle, prState, action, externalId, now, ct);
            }
        }

        return await RecordDeliveryAsync(
            integration,
            deliveryId,
            "pull_request",
            action,
            repoFullName,
            "Processed",
            $"Xử lý thành công PR #{prNumber} ({prState}) cho {tasks.Count} thẻ: {string.Join(", ", tasks.Select(t => $"TN-{t.TaskNumber}"))}.",
            ct);
    }

    private async Task ApplyPrStateMachineAsync(
        WorkspaceGitHubIntegration integration,
        BoardTask task,
        long prNumber,
        string? repoFullName,
        string prTitle,
        string prState,
        string? action,
        string externalId,
        DateTimeOffset now,
        CancellationToken ct)
    {
        if (task.Column is null)
        {
            return;
        }

        if (string.Equals(prState, "open", StringComparison.OrdinalIgnoreCase))
        {
            if (!task.Column.IsInReview && !task.Column.IsDone)
            {
                var moveResult = await _taskAutomation.MoveToSystemColumnAsync(
                    task.Id,
                    SystemColumnTarget.InReview,
                    $"GitHub PR #{prNumber} ({repoFullName}) mở đề xuất review",
                    ct);

                if (moveResult.Moved)
                {
                    await RecordAiActionLogAsync(
                        task,
                        moveResult,
                        repoFullName,
                        prNumber,
                        prTitle,
                        prState,
                        action,
                        now,
                        ct);
                }
            }
        }
        else if (string.Equals(prState, "merged", StringComparison.OrdinalIgnoreCase))
        {
            if (!task.Column.IsDone)
            {
                var hasOtherActivePrs = await _db.TaskExternalLinks
                    .AsNoTracking()
                    .AnyAsync(l => l.TaskId == task.Id
                                   && l.Provider == "github"
                                   && l.LinkType == "pull_request"
                                   && l.ExternalId != externalId
                                   && (l.State == "open" || l.State == "draft"), ct);

                if (!hasOtherActivePrs)
                {
                    var moveResult = await _taskAutomation.MoveToSystemColumnAsync(
                        task.Id,
                        SystemColumnTarget.Done,
                        $"GitHub PR #{prNumber} ({repoFullName}) đã được hợp nhất (merged)",
                        ct);

                    if (moveResult.Moved)
                    {
                        await RecordAiActionLogAsync(
                            task,
                            moveResult,
                            repoFullName,
                            prNumber,
                            prTitle,
                            prState,
                            action,
                            now,
                            ct);
                    }
                }
            }
        }
    }

    private async Task RecordAiActionLogAsync(
        BoardTask task,
        TaskAutomationMoveResult moveResult,
        string? repoFullName,
        long prNumber,
        string prTitle,
        string prState,
        string? action,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var log = new AiActionLog
        {
            Action = AiActionTypes.GitHubPrMoveTask,
            EntityType = AiEntityTypes.Board,
            EntityId = task.BoardId,
            Status = AiActionStatus.Approved,
            RequestedByUserId = SystemUsers.GitHubBotUserId,
            DecidedByUserId = SystemUsers.GitHubBotUserId,
            DecidedAt = now,
            Basis = JsonSerializer.Serialize(new
            {
                repo = repoFullName,
                prNumber,
                prTitle,
                state = prState,
                action,
            }, JsonOptions),
            BeforeSnapshot = JsonSerializer.Serialize(new
            {
                taskId = task.Id,
                columnId = moveResult.FromColumnId,
                position = moveResult.PreviousPosition,
                completedAt = moveResult.PreviousCompletedAt,
            }, JsonOptions),
            AfterSnapshot = JsonSerializer.Serialize(new
            {
                taskId = task.Id,
                columnId = moveResult.ToColumnId,
                position = moveResult.NewPosition,
                completedAt = moveResult.NewCompletedAt,
            }, JsonOptions),
            AppliedSnapshot = JsonSerializer.Serialize(new
            {
                updatedTaskId = task.Id,
                fromColumnId = moveResult.FromColumnId,
                toColumnId = moveResult.ToColumnId,
                previousPosition = moveResult.PreviousPosition,
                previousCompletedAt = moveResult.PreviousCompletedAt,
            }, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now,
        };

        _db.AiActionLogs.Add(log);
        await _db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "AiActionLog recorded for GitHub PR move (LogId {LogId}, TaskId {TaskId}, From {FromColumn} To {ToColumn}).",
            log.Id,
            task.Id,
            moveResult.FromColumnId,
            moveResult.ToColumnId);
    }

    private async Task<GitHubWebhookProcessResult> RecordDeliveryAsync(
        WorkspaceGitHubIntegration integration,
        string deliveryId,
        string eventName,
        string? action,
        string? repoFullName,
        string status,
        string summary,
        CancellationToken ct)
    {
        var delivery = new GitHubWebhookDelivery
        {
            IntegrationId = integration.Id,
            WorkspaceId = integration.WorkspaceId,
            DeliveryId = deliveryId,
            EventName = eventName,
            Action = action,
            RepositoryFullName = repoFullName,
            Status = status,
            Summary = summary,
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        _db.GitHubWebhookDeliveries.Add(delivery);
        await SaveDeliverySafelyAsync(integration.Id, deliveryId, ct);

        return new GitHubWebhookProcessResult(status, Summary: summary);
    }

    private async Task SaveDeliverySafelyAsync(Guid integrationId, string deliveryId, CancellationToken ct)
    {
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to save GitHubWebhookDelivery (IntegrationId {IntegrationId}, DeliveryId {DeliveryId}). Checking duplicate.",
                integrationId,
                deliveryId);

            var duplicate = await _db.GitHubWebhookDeliveries
                .AnyAsync(d => d.IntegrationId == integrationId && d.DeliveryId == deliveryId, ct);

            if (!duplicate)
            {
                throw;
            }
        }
    }
}
