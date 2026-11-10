namespace TeamNexus.Modules.Board.Services.Integrations;

using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Handles incoming GitHub webhook payloads (Phase 15 §1.3).
/// Verifies HMAC-SHA256 signatures, logs deliveries for idempotency,
/// and completes Kanban tasks when linked Pull Requests are merged.
/// </summary>
public partial class GitHubWebhookHandler : IGitHubWebhookHandler
{
    private readonly TeamNexusDbContext _db;
    private readonly IIntegrationSecretProtector _protector;
    private readonly ITaskService _taskService;
    private readonly ILogger<GitHubWebhookHandler> _logger;

    public GitHubWebhookHandler(
        TeamNexusDbContext db,
        IIntegrationSecretProtector protector,
        ITaskService taskService,
        ILogger<GitHubWebhookHandler> logger)
    {
        _db = db;
        _protector = protector;
        _taskService = taskService;
        _logger = logger;
    }

    public async Task<IResult> HandleWebhookAsync(
        string? eventType,
        string? signature,
        string? deliveryId,
        byte[] rawBody,
        CancellationToken ct = default)
    {
        // A. Kiểm tra rawBody
        if (rawBody == null || rawBody.Length == 0)
        {
            return Results.BadRequest(new { error = "Empty body" });
        }

        // B. Parse JSON payload
        JsonDocument payload;
        try
        {
            payload = JsonDocument.Parse(rawBody);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse webhook JSON payload.");
            return Results.BadRequest(new { error = "Invalid repository payload" });
        }

        using (payload)
        {
            var root = payload.RootElement;
            if (!root.TryGetProperty("repository", out var repoElem) ||
                !repoElem.TryGetProperty("full_name", out var fnElem) ||
                string.IsNullOrWhiteSpace(fnElem.GetString()))
            {
                _logger.LogWarning("Webhook payload missing 'repository.full_name'.");
                return Results.BadRequest(new { error = "Invalid repository payload" });
            }

            var repoFullName = fnElem.GetString()!;

            // C. Tìm WorkspaceIntegration liên kết với repo đó
            var integration = await FindIntegrationAsync(repoFullName, ct);
            if (integration == null)
            {
                _logger.LogInformation("Repository '{Repo}' not registered or integration disabled; event ignored.", repoFullName);
                return Results.Ok(new { message = "Repository not registered or integration disabled; event ignored." });
            }

            // D. Xác thực chữ ký HMAC-SHA256
            if (!string.IsNullOrEmpty(integration.EncryptedWebhookSecret))
            {
                if (string.IsNullOrWhiteSpace(signature))
                {
                    _logger.LogWarning("Missing X-Hub-Signature-256 header for repository '{Repo}'.", repoFullName);
                    return Results.Unauthorized();
                }

                string secret;
                try
                {
                    secret = _protector.Unprotect(integration.EncryptedWebhookSecret);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to decrypt webhook secret for repository '{Repo}'.", repoFullName);
                    return Results.Unauthorized();
                }

                if (!GitHubSignatureValidator.ValidateSignature(rawBody, signature, secret))
                {
                    _logger.LogWarning("Invalid HMAC-SHA256 signature for repository '{Repo}'.", repoFullName);
                    return Results.Unauthorized();
                }
            }
            else
            {
                _logger.LogWarning("Workspace integration {IntegrationId} for repository '{Repo}' has no webhook secret configured; processing payload unverified.", integration.Id, repoFullName);
            }

            // E. Kiểm tra Idempotency với deliveryId
            if (!string.IsNullOrEmpty(deliveryId))
            {
                var alreadyProcessed = await IsDeliveryProcessedAsync(deliveryId, ct);
                if (alreadyProcessed)
                {
                    _logger.LogInformation("Webhook delivery '{DeliveryId}' already processed; returning 200.", deliveryId);
                    return Results.Ok(new { message = "Webhook delivery already processed." });
                }

                var deliveryLog = new WebhookDeliveryLog
                {
                    DeliveryId = deliveryId,
                    Provider = "github",
                    EventType = string.IsNullOrWhiteSpace(eventType) ? "unknown" : eventType,
                    ProcessedAt = DateTimeOffset.UtcNow,
                    Status = "Processed"
                };

                await RecordDeliveryLogAsync(deliveryLog, ct);
            }

            // F. Xử lý các loại sự kiện (eventType)
            if (string.Equals(eventType, "ping", StringComparison.OrdinalIgnoreCase))
            {
                var zen = root.TryGetProperty("zen", out var zenElem) && zenElem.GetString() is { } z
                    ? z
                    : "Keep it logically awesome.";
                return Results.Ok(new { message = "pong", zen });
            }

            if (string.Equals(eventType, "pull_request", StringComparison.OrdinalIgnoreCase))
            {
                if (!root.TryGetProperty("action", out var actionElem) || actionElem.ValueKind != JsonValueKind.String)
                {
                    return Results.Ok(new { message = "Pull request payload missing action; event ignored." });
                }

                var action = actionElem.GetString() ?? "";

                if (!root.TryGetProperty("pull_request", out var prElem) || prElem.ValueKind != JsonValueKind.Object)
                {
                    return Results.Ok(new { message = "Pull request payload missing pull_request object; event ignored." });
                }

                var isMerged = prElem.TryGetProperty("merged", out var mergedElem) &&
                               mergedElem.ValueKind == JsonValueKind.True;

                if (!string.Equals(action, "closed", StringComparison.OrdinalIgnoreCase) || !isMerged)
                {
                    return Results.Ok(new { message = $"Pull request action '{action}' noted, no task transition required." });
                }

                // PR đã merged -> lấy thông tin PR
                var prNumber = prElem.TryGetProperty("number", out var numElem) && numElem.TryGetInt32(out var n) ? n : 0;
                var prUrl = prElem.TryGetProperty("html_url", out var urlElem) ? urlElem.GetString() ?? "" : "";
                var prTitle = prElem.TryGetProperty("title", out var titleElem) ? titleElem.GetString() ?? "" : "";
                var prBody = prElem.TryGetProperty("body", out var bodyElem) ? bodyElem.GetString() ?? "" : "";
                var branchName = prElem.TryGetProperty("head", out var headElem) &&
                                 headElem.TryGetProperty("ref", out var refElem)
                    ? refElem.GetString() ?? ""
                    : "";

                // Tìm Task liên kết:
                // 1. Cách 1: Tìm bằng prNumber trong cùng workspace
                var matchedTasks = await FindTasksByPrNumberAsync(integration.WorkspaceId, prNumber, ct);

                // 2. Cách 2: Nếu không thấy bằng prNumber, tìm Task ID dạng UUID trong prTitle, prBody, branchName
                if (matchedTasks.Count == 0)
                {
                    var candidateGuids = new HashSet<Guid>();
                    var textsToSearch = new[] { prTitle, prBody, branchName };
                    foreach (var text in textsToSearch)
                    {
                        if (string.IsNullOrEmpty(text)) continue;
                        foreach (Match match in UuidRegex().Matches(text))
                        {
                            if (Guid.TryParse(match.Value, out var guid))
                            {
                                candidateGuids.Add(guid);
                            }
                        }
                    }

                    if (candidateGuids.Count > 0)
                    {
                        matchedTasks = await FindTasksByIdsAsync(integration.WorkspaceId, candidateGuids, ct);
                    }
                }

                // 3. Cách 3: Nếu không thấy bằng UUID, tìm bằng mã rút gọn Hex 8 ký tự (ví dụ #F3FFE732, F3FFE732)
                if (matchedTasks.Count == 0)
                {
                    var candidateShortCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var textsToSearch = new[] { prTitle, prBody, branchName };
                    foreach (var text in textsToSearch)
                    {
                        if (string.IsNullOrEmpty(text)) continue;
                        foreach (Match match in ShortHexRegex().Matches(text))
                        {
                            candidateShortCodes.Add(match.Groups[1].Value);
                        }
                    }

                    if (candidateShortCodes.Count > 0)
                    {
                        matchedTasks = await FindTasksByShortCodesAsync(integration.WorkspaceId, candidateShortCodes, ct);
                    }
                }

                if (matchedTasks.Count == 0)
                {
                    return Results.Ok(new { message = $"No associated board task found for PR #{prNumber} in workspace." });
                }

                var matchedTaskIds = new List<Guid>();
                foreach (var task in matchedTasks)
                {
                    try
                    {
                        await _taskService.CompleteTaskViaGitHubAsync(task.Id, prUrl, prNumber, ct);
                        matchedTaskIds.Add(task.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to complete task {TaskId} via GitHub webhook for PR #{PrNumber}.", task.Id, prNumber);
                    }
                }

                return Results.Ok(new { message = $"Tasks transitioned to Done: {matchedTaskIds.Count}", taskIds = matchedTaskIds });
            }

            return Results.Ok(new { message = $"Event '{eventType}' received and logged." });
        }
    }

    protected virtual async Task<WorkspaceIntegration?> FindIntegrationAsync(string repoFullName, CancellationToken ct)
    {
        var normalized = repoFullName.ToLowerInvariant();
        return await _db.WorkspaceIntegrations
            .AsNoTracking()
            .FirstOrDefaultAsync(i => i.IsEnabled && i.Provider == "github" && i.RepoName != null && i.RepoName.ToLower() == normalized, ct);
    }

    protected virtual async Task<bool> IsDeliveryProcessedAsync(string deliveryId, CancellationToken ct)
    {
        return await _db.WebhookDeliveryLogs
            .AsNoTracking()
            .AnyAsync(l => l.DeliveryId == deliveryId, ct);
    }

    protected virtual async Task RecordDeliveryLogAsync(WebhookDeliveryLog deliveryLog, CancellationToken ct)
    {
        _db.WebhookDeliveryLogs.Add(deliveryLog);
        await _db.SaveChangesAsync(ct);
    }

    protected virtual async Task<List<BoardTask>> FindTasksByPrNumberAsync(Guid workspaceId, int prNumber, CancellationToken ct)
    {
        return await _db.Tasks
            .Include(t => t.Board)
            .Where(t => t.DeletedAt == null
                && t.Board != null
                && t.Board.WorkspaceId == workspaceId
                && t.GithubPrNumber == prNumber)
            .ToListAsync(ct);
    }

    protected virtual async Task<List<BoardTask>> FindTasksByIdsAsync(Guid workspaceId, IReadOnlyCollection<Guid> taskIds, CancellationToken ct)
    {
        return await _db.Tasks
            .Include(t => t.Board)
            .Where(t => t.DeletedAt == null
                && t.Board != null
                && t.Board.WorkspaceId == workspaceId
                && taskIds.Contains(t.Id))
            .ToListAsync(ct);
    }

    protected virtual async Task<List<BoardTask>> FindTasksByShortCodesAsync(Guid workspaceId, IReadOnlyCollection<string> shortCodes, CancellationToken ct)
    {
        var normalizedCodes = shortCodes.Select(c => c.ToLowerInvariant()).ToHashSet();
        var allWorkspaceTasks = await _db.Tasks
            .Include(t => t.Board)
            .Where(t => t.DeletedAt == null
                && t.Board != null
                && t.Board.WorkspaceId == workspaceId)
            .ToListAsync(ct);

        return allWorkspaceTasks
            .Where(t => normalizedCodes.Contains(t.Id.ToString("N")[..8].ToLowerInvariant()))
            .ToList();
    }

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.CultureInvariant)]
    private static partial Regex UuidRegex();

    [GeneratedRegex(@"(?<=^|[\s/#_(\[<{:-])([0-9a-fA-F]{8})(?=$|[\s/#_)\]>},;:.])", RegexOptions.CultureInvariant)]
    private static partial Regex ShortHexRegex();
}
