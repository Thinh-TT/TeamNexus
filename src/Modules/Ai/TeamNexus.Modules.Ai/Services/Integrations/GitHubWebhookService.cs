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
    private readonly ITaskTriageService _triageService;
    private readonly TaskKeyParser _taskKeyParser;
    private readonly ILogger<GitHubWebhookService> _logger;

    public GitHubWebhookService(
        TeamNexusDbContext db,
        ITaskAutomation taskAutomation,
        ITaskTriageService triageService,
        TaskKeyParser taskKeyParser,
        ILogger<GitHubWebhookService> logger)
    {
        _db = db;
        _taskAutomation = taskAutomation;
        _triageService = triageService;
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

        if (string.Equals(eventName, "issues", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleIssuesEventAsync(integration, deliveryId, rawBody, ct);
        }

        if (string.Equals(eventName, "issue_comment", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleIssueCommentEventAsync(integration, deliveryId, rawBody, ct);
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

        // `requireTnPrefix: true` — a bare `#123` in a PR refers to a GitHub issue/PR number, and acting on
        // it would move whichever TeamNexus task happens to carry number 123 (review F5).
        var taskNumbers = _taskKeyParser.ParseTaskNumbers([prTitle, headRef, prBody], maxCount: 5, requireTnPrefix: true);

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

    private async Task<GitHubWebhookProcessResult> HandleIssuesEventAsync(
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

        if (!root.TryGetProperty("issue", out var issue))
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "issues",
                action,
                repoFullName,
                "Ignored",
                "Payload issues không chứa thông tin issue.",
                ct);
        }

        var issueNumber = issue.TryGetProperty("number", out var inum) ? inum.GetInt64() : 0L;
        var issueTitle = issue.TryGetProperty("title", out var it) ? it.GetString() ?? string.Empty : string.Empty;
        var issueHtmlUrl = issue.TryGetProperty("html_url", out var ih) ? ih.GetString() ?? string.Empty : string.Empty;
        var issueBody = issue.TryGetProperty("body", out var ib) ? ib.GetString() : null;

        var senderLogin = root.TryGetProperty("sender", out var sender)
                          && sender.ValueKind == JsonValueKind.Object
                          && sender.TryGetProperty("login", out var sl)
            ? sl.GetString() ?? "unknown"
            : "unknown";

        var externalId = $"{repoFullName}#{issueNumber}";
        var now = DateTimeOffset.UtcNow;

        if (string.Equals(action, "closed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "reopened", StringComparison.OrdinalIgnoreCase))
        {
            var newState = string.Equals(action, "closed", StringComparison.OrdinalIgnoreCase) ? "closed" : "open";
            var existingLink = await _db.TaskExternalLinks
                .FirstOrDefaultAsync(l => l.WorkspaceId == integration.WorkspaceId
                                          && l.Provider == "github"
                                          && l.LinkType == "issue"
                                          && l.ExternalId == externalId, ct);

            if (existingLink is not null)
            {
                existingLink.State = newState;
                existingLink.UpdatedAt = now;
                await _db.SaveChangesAsync(ct);

                // Phase 15.4 §T15.4.2(4): the chip on the card must flip without a manual refresh.
                await _taskAutomation.NotifyTaskUpdatedAsync(existingLink.TaskId, ct);

                return await RecordDeliveryAsync(
                    integration,
                    deliveryId,
                    "issues",
                    action,
                    repoFullName,
                    "Processed",
                    $"Đã cập nhật trạng thái liên kết Issue #{issueNumber} thành '{newState}'.",
                    ct);
            }

            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "issues",
                action,
                repoFullName,
                "Ignored",
                $"Issue #{issueNumber} chưa được liên kết với bất kỳ thẻ nào trong workspace.",
                ct);
        }

        if (string.Equals(action, "opened", StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, "labeled", StringComparison.OrdinalIgnoreCase))
        {
            if (!integration.AutoCreateTasksFromIssues)
            {
                return await RecordDeliveryAsync(
                    integration,
                    deliveryId,
                    "issues",
                    action,
                    repoFullName,
                    "Ignored",
                    "Tính năng tự động tạo thẻ từ Issue đang bị tắt.",
                    ct);
            }

            if (!integration.DefaultBoardId.HasValue)
            {
                return await RecordDeliveryAsync(
                    integration,
                    deliveryId,
                    "issues",
                    action,
                    repoFullName,
                    "Ignored",
                    "Chưa cấu hình bảng mặc định (DefaultBoardId) để tạo thẻ từ Issue.",
                    ct);
            }

            var boardExists = await _db.Boards
                .AsNoTracking()
                .AnyAsync(b => b.Id == integration.DefaultBoardId.Value && b.WorkspaceId == integration.WorkspaceId, ct);

            if (!boardExists)
            {
                return await RecordDeliveryAsync(
                    integration,
                    deliveryId,
                    "issues",
                    action,
                    repoFullName,
                    "Failed",
                    $"Bảng mặc định ({integration.DefaultBoardId.Value}) không tồn tại trong workspace.",
                    ct);
            }

            // Check IssueLabelFilter
            if (!string.IsNullOrWhiteSpace(integration.IssueLabelFilter))
            {
                var filterLabels = integration.IssueLabelFilter
                    .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var issueLabels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (issue.TryGetProperty("labels", out var labelsElement) && labelsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var lbl in labelsElement.EnumerateArray())
                    {
                        if (lbl.ValueKind == JsonValueKind.Object
                            && lbl.TryGetProperty("name", out var ln)
                            && ln.GetString() is { } name)
                        {
                            issueLabels.Add(name);
                        }
                    }
                }

                if (!issueLabels.Overlaps(filterLabels))
                {
                    return await RecordDeliveryAsync(
                        integration,
                        deliveryId,
                        "issues",
                        action,
                        repoFullName,
                        "Ignored",
                        $"Issue #{issueNumber} không chứa nhãn nào thuộc bộ lọc [{integration.IssueLabelFilter}].",
                        ct);
                }
            }

            // Deduplication: check if already linked to an active task
            var existingLink = await _db.TaskExternalLinks
                .Include(l => l.Task)
                .FirstOrDefaultAsync(l => l.WorkspaceId == integration.WorkspaceId
                                          && l.Provider == "github"
                                          && l.LinkType == "issue"
                                          && l.ExternalId == externalId, ct);

            if (existingLink?.Task is not null && existingLink.Task.DeletedAt == null)
            {
                return await RecordDeliveryAsync(
                    integration,
                    deliveryId,
                    "issues",
                    action,
                    repoFullName,
                    "Ignored",
                    $"Issue #{issueNumber} ({repoFullName}) đã được liên kết với thẻ TN-{existingLink.Task.TaskNumber}, bỏ qua tạo mới.",
                    ct);
            }

            // Create task
            var taskTitle = $"[GH #{issueNumber}] {issueTitle}";
            var taskDescription = FormatIssueDescription(issueBody, issueHtmlUrl, senderLogin, repoFullName, issueNumber);

            var createdTask = await _taskAutomation.CreateTaskFromExternalIssueAsync(
                integration.DefaultBoardId.Value,
                taskTitle,
                taskDescription,
                ct);

            // Upsert TaskExternalLink
            if (existingLink is null)
            {
                existingLink = new TaskExternalLink
                {
                    WorkspaceId = integration.WorkspaceId,
                    TaskId = createdTask.Id,
                    Provider = "github",
                    LinkType = "issue",
                    ExternalId = externalId,
                    ExternalUrl = issueHtmlUrl,
                    Title = issueTitle,
                    State = "open",
                    CreatedAt = now,
                    UpdatedAt = now,
                };
                _db.TaskExternalLinks.Add(existingLink);
            }
            else
            {
                existingLink.TaskId = createdTask.Id;
                existingLink.Title = issueTitle;
                existingLink.ExternalUrl = issueHtmlUrl;
                existingLink.State = "open";
                existingLink.UpdatedAt = now;
            }

            await _db.SaveChangesAsync(ct);

            // Record AiActionLog for accountability & Undo BEFORE the optional AI enrichment.
            //
            // Review F7: Jev/tag application used to run first. If it threw, the task already existed but no
            // audit row was ever written — and because a redelivery is deduplicated by the external link,
            // the card stayed permanently un-undoable. The accountability row is the non-negotiable part;
            // the suggestions are best-effort.
            var log = new AiActionLog
            {
                Action = AiActionTypes.GitHubIssueCreateTask,
                EntityType = AiEntityTypes.Board,
                EntityId = integration.DefaultBoardId.Value,
                Status = AiActionStatus.Approved,
                RequestedByUserId = SystemUsers.GitHubBotUserId,
                DecidedByUserId = SystemUsers.GitHubBotUserId,
                DecidedAt = now,
                Basis = JsonSerializer.Serialize(new
                {
                    repo = repoFullName,
                    issueNumber,
                    issueTitle,
                    sender = senderLogin,
                    action,
                }, JsonOptions),
                BeforeSnapshot = JsonSerializer.Serialize(new { }, JsonOptions),
                AfterSnapshot = JsonSerializer.Serialize(new
                {
                    createdTaskIds = new[] { createdTask.Id },
                    boardId = integration.DefaultBoardId.Value,
                }, JsonOptions),
                AppliedSnapshot = JsonSerializer.Serialize(new
                {
                    createdTaskIds = new[] { createdTask.Id },
                    boardId = integration.DefaultBoardId.Value,
                }, JsonOptions),
                CreatedAt = now,
                UpdatedAt = now,
            };

            _db.AiActionLogs.Add(log);
            await _db.SaveChangesAsync(ct);

            // Jev 1.13 Auto-Triage (fail-soft): the task already exists, so an AI/tagging failure must never
            // turn the whole delivery into a 500 — it only means "no suggestions were applied".
            try
            {
                var triage = await _triageService.TriageForSystemAsync(
                    integration.DefaultBoardId.Value,
                    issueTitle,
                    issueBody,
                    ct);

                if (triage is not null && !triage.Degraded && triage.ConfidenceScore >= 0.50)
                {
                    var tags = new List<string>();
                    if (!string.IsNullOrWhiteSpace(triage.SuggestedDomain))
                    {
                        tags.Add(triage.SuggestedDomain);
                    }
                    if (!string.IsNullOrWhiteSpace(triage.SuggestedWorkType))
                    {
                        tags.Add(triage.SuggestedWorkType);
                    }

                    await _taskAutomation.ApplySystemTriageSuggestionsAsync(
                        createdTask.Id,
                        triage.SuggestedPriority,
                        estimatedHours: null,
                        tags: tags.Count > 0 ? tags : null,
                        assigneeId: null,
                        ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    ex,
                    "Jev auto-triage skipped for TN-{TaskNumber} (Issue #{IssueNumber}); task and audit log were kept.",
                    createdTask.TaskNumber,
                    issueNumber);
            }

            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "issues",
                action,
                repoFullName,
                "Processed",
                $"Đã tạo thẻ mới TN-{createdTask.TaskNumber} từ Issue #{issueNumber} ({repoFullName}).",
                ct);
        }

        return await RecordDeliveryAsync(
            integration,
            deliveryId,
            "issues",
            action,
            repoFullName,
            "Ignored",
            $"Bỏ qua sự kiện issues action '{action}'.",
            ct);
    }

    private async Task<GitHubWebhookProcessResult> HandleIssueCommentEventAsync(
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

        if (!string.Equals(action, "created", StringComparison.OrdinalIgnoreCase))
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "issue_comment",
                action,
                repoFullName,
                "Ignored",
                $"Bỏ qua sự kiện bình luận với action '{action}'.",
                ct);
        }

        if (!integration.SyncIssueComments)
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "issue_comment",
                action,
                repoFullName,
                "Ignored",
                "Tính năng đồng bộ bình luận GitHub đang bị tắt.",
                ct);
        }

        // Anti-bot & anti-loop: ignore bot comments. Both defensively typed: GitHub always sends an object,
        // but a `null`/scalar `sender` would make TryGetProperty throw and answer 500 (review F13).
        var senderObject = root.TryGetProperty("sender", out var senderElement)
                           && senderElement.ValueKind == JsonValueKind.Object
            ? senderElement
            : default;

        var senderType = senderObject.ValueKind == JsonValueKind.Object
                         && senderObject.TryGetProperty("type", out var st)
            ? st.GetString()
            : null;

        if (string.Equals(senderType, "Bot", StringComparison.OrdinalIgnoreCase))
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "issue_comment",
                action,
                repoFullName,
                "Ignored",
                "Bỏ qua bình luận từ Bot để chống vòng lặp.",
                ct);
        }

        if (!root.TryGetProperty("comment", out var comment))
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "issue_comment",
                action,
                repoFullName,
                "Ignored",
                "Payload không chứa dữ liệu comment.",
                ct);
        }

        var commentBody = comment.TryGetProperty("body", out var cb) ? cb.GetString() : null;
        if (string.IsNullOrWhiteSpace(commentBody))
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "issue_comment",
                action,
                repoFullName,
                "Ignored",
                "Nội dung bình luận trống.",
                ct);
        }

        var commentHtmlUrl = comment.TryGetProperty("html_url", out var ch) ? ch.GetString() ?? string.Empty : string.Empty;
        var senderLogin = senderObject.ValueKind == JsonValueKind.Object
                          && senderObject.TryGetProperty("login", out var sl)
            ? sl.GetString() ?? "unknown"
            : "unknown";

        var issueNumber = root.TryGetProperty("issue", out var issue)
                          && issue.TryGetProperty("number", out var inum)
            ? inum.GetInt64()
            : 0L;

        // Resolve target tasks
        var targetTaskIds = new HashSet<Guid>();

        // Source 1: From task_external_links
        if (issueNumber > 0 && !string.IsNullOrWhiteSpace(repoFullName))
        {
            var externalId = $"{repoFullName}#{issueNumber}";
            var linkedTaskIds = await _db.TaskExternalLinks
                .Where(l => l.WorkspaceId == integration.WorkspaceId
                            && l.Provider == "github"
                            && l.ExternalId == externalId)
                .Select(l => l.TaskId)
                .ToListAsync(ct);

            foreach (var id in linkedTaskIds)
            {
                targetTaskIds.Add(id);
            }
        }

        // Source 2: Parse TN-* from commentBody. `#123` is excluded on purpose — in a GitHub comment it
        // references an issue/PR, not a TeamNexus task (review F5).
        var taskNumbers = _taskKeyParser.ParseTaskNumbers([commentBody], maxCount: 5, requireTnPrefix: true);
        if (taskNumbers.Count > 0)
        {
            var referencedTaskIds = await _db.Tasks
                .Where(t => t.Board != null
                            && t.Board.WorkspaceId == integration.WorkspaceId
                            && taskNumbers.Contains(t.TaskNumber))
                .Select(t => t.Id)
                .ToListAsync(ct);

            foreach (var id in referencedTaskIds)
            {
                targetTaskIds.Add(id);
            }
        }

        if (targetTaskIds.Count == 0)
        {
            return await RecordDeliveryAsync(
                integration,
                deliveryId,
                "issue_comment",
                action,
                repoFullName,
                "Ignored",
                $"Không tìm thấy thẻ liên kết hoặc mã TN-* để đồng bộ bình luận từ Issue #{issueNumber}.",
                ct);
        }

        // Format markdown comment
        var trimmed = commentBody.Trim();
        var truncated = trimmed.Length <= 1800 ? trimmed : trimmed[..1800] + "...";
        var quotedBody = string.Join("\n", truncated.Split('\n').Select(line => $"> {line}"));

        var formattedMarkdown = $"""
            💬 **@{senderLogin}** đã bình luận trên GitHub ([#{issueNumber}]({commentHtmlUrl})):

            {quotedBody}
            """;

        foreach (var taskId in targetTaskIds)
        {
            await _taskAutomation.AddSystemCommentAsync(
                taskId,
                SystemUsers.GitHubBotUserId,
                formattedMarkdown,
                ct);
        }

        return await RecordDeliveryAsync(
            integration,
            deliveryId,
            "issue_comment",
            action,
            repoFullName,
            "Processed",
            $"Đã đồng bộ bình luận từ @{senderLogin} vào {targetTaskIds.Count} thẻ.",
            ct);
    }

    private static string FormatIssueDescription(
        string? issueBody,
        string issueHtmlUrl,
        string senderLogin,
        string? repoFullName,
        long issueNumber)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"🔗 **GitHub Issue #{issueNumber}** ({repoFullName}) — mở bởi @{senderLogin}");
        sb.AppendLine($"Xem chi tiết: {issueHtmlUrl}");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(issueBody))
        {
            sb.AppendLine(issueBody.Trim());
        }
        return sb.ToString();
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
