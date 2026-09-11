using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent.Agents;

/// <summary>
/// <c>SearchSystemData</c> (Phase 7 §4.4): the only tool that reads business data, so its boundaries
/// are explicit.
/// <list type="bullet">
///   <item>Scope ids come from <see cref="AgentToolContext"/> only — <c>workspace</c> scope resolves
///   the workspace's boards server-side, so a model-supplied id cannot widen it (risk R7).</item>
///   <item>Never returns <c>bytea</c> attachment content, <c>ai_action_logs</c>, <c>agent_runs</c>, or
///   any user outside the workspace.</item>
///   <item>Excerpts are pre-trimmed and the whole payload is capped at
///   <c>Agent:MaxToolResultChars</c>, degrading to a compact list before ever truncating JSON.</item>
/// </list>
/// </summary>
public sealed class SearchSystemDataTool : IAgentTool
{
    /// <summary>Characters kept per task description (token control).</summary>
    private const int DescriptionExcerptChars = 300;

    /// <summary>Characters kept per "latest comment" excerpt.</summary>
    private const int CommentExcerptChars = 200;

    /// <summary>Upper bound on comments loaded to find the newest one per task.</summary>
    private const int CommentScanLimit = 400;

    private const int DefaultLimit = 10;

    private const int MaxLimit = 20;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly AgentEffectiveOptions _options;

    public SearchSystemDataTool(TeamNexusDbContext db, IOptions<AgentOptions> options)
    {
        _db = db;
        _options = options.Value.Effective;
    }

    public string Name => AgentToolDefinitions.SearchSystemDataName;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var scope = (ToolArguments.GetTrimmedString(arguments, "scope") ?? "board").ToLowerInvariant();
        if (!AgentToolDefinitions.SearchScopes.Contains(scope, StringComparer.OrdinalIgnoreCase))
        {
            return AgentToolRegistry.Error(
                $"scope must be one of: {string.Join(", ", AgentToolDefinitions.SearchScopes)}.");
        }

        var query = ToolArguments.GetTrimmedString(arguments, "query");
        var limit = Math.Clamp(ToolArguments.GetInt(arguments, "limit") ?? DefaultLimit, 1, MaxLimit);

        var tasks = _db.Tasks.AsNoTracking();

        switch (scope)
        {
            case "task":
                tasks = tasks.Where(t => t.Id == context.TaskId);
                break;

            case "workspace":
                // Resolved from the context, never from the arguments: this IS the cross-workspace guard.
                var boardIds = await _db.Boards
                    .AsNoTracking()
                    .Where(b => b.WorkspaceId == context.WorkspaceId)
                    .Select(b => b.Id)
                    .ToListAsync(ct);

                tasks = tasks.Where(t => boardIds.Contains(t.BoardId));
                break;

            default:
                tasks = tasks.Where(t => t.BoardId == context.BoardId);
                break;
        }

        if (query is not null)
        {
            var pattern = $"%{query}%";
            tasks = tasks.Where(t => EF.Functions.ILike(t.Title, pattern));
        }

        var rows = await tasks
            .OrderBy(t => t.Position)
            .ThenBy(t => t.CreatedAt)
            .Take(limit)
            .Select(t => new TaskRow(
                t.Id,
                t.Title,
                t.Description,
                t.Priority,
                t.DueDate,
                t.UpdatedAt,
                t.Column != null ? t.Column.Name : null,
                t.Column != null && t.Column.IsDone,
                t.Assignee != null ? t.Assignee.DisplayName : null))
            .ToListAsync(ct);

        var taskIds = rows.Select(r => r.Id).ToList();
        var latestComments = await LoadLatestCommentsAsync(taskIds, ct);

        var payload = JsonSerializer.Serialize(new
        {
            scope,
            count = rows.Count,
            tasks = rows.Select(r => new
            {
                id = r.Id,
                title = r.Title,
                description = Excerpt(r.Description, DescriptionExcerptChars),
                column = r.ColumnName,
                isDone = r.IsDone,
                assignee = r.AssigneeName,
                priority = r.Priority?.ToString(),
                dueDate = r.DueDate,
                updatedAt = r.UpdatedAt,
                lastComment = latestComments.TryGetValue(r.Id, out var comment)
                    ? new { author = comment.Author, at = comment.CreatedAt, excerpt = Excerpt(comment.Content, CommentExcerptChars) }
                    : null,
            }),
        }, Json);

        if (payload.Length > _options.MaxToolResultChars)
        {
            payload = JsonSerializer.Serialize(new
            {
                scope,
                count = rows.Count,
                tasks = rows.Select(r => new { id = r.Id, title = Excerpt(r.Title, 120), column = r.ColumnName }),
            }, Json);
        }

        if (payload.Length > _options.MaxToolResultChars)
        {
            payload = JsonSerializer.Serialize(new
            {
                scope,
                count = rows.Count,
                note = "Dữ liệu quá dài, chỉ trả về id và tiêu đề.",
                tasks = rows.Select(r => new { id = r.Id, title = Excerpt(r.Title, 60) }),
            }, Json);
        }

        return payload;
    }

    /// <summary>
    /// Newest comment per task. Bounded scan (then grouped in memory) rather than a correlated
    /// subquery: the page is at most 20 tasks and this keeps the generated SQL simple and portable.
    /// </summary>
    private async Task<Dictionary<Guid, CommentRow>> LoadLatestCommentsAsync(
        List<Guid> taskIds, CancellationToken ct)
    {
        if (taskIds.Count == 0)
        {
            return [];
        }

        var comments = await _db.TaskComments
            .AsNoTracking()
            .Where(c => taskIds.Contains(c.TaskId))
            .OrderByDescending(c => c.CreatedAt)
            .Take(CommentScanLimit)
            .Select(c => new CommentRow(
                c.TaskId,
                c.CreatedAt,
                c.Author != null ? c.Author.DisplayName : string.Empty,
                c.Content))
            .ToListAsync(ct);

        var result = new Dictionary<Guid, CommentRow>();
        foreach (var comment in comments)
        {
            result.TryAdd(comment.TaskId, comment);
        }

        return result;
    }

    private static string? Excerpt(string? value, int limit)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= limit ? trimmed : trimmed[..limit] + "…";
    }

    private sealed record TaskRow(
        Guid Id,
        string Title,
        string? Description,
        TaskPriority? Priority,
        DateTimeOffset? DueDate,
        DateTimeOffset UpdatedAt,
        string? ColumnName,
        bool IsDone,
        string? AssigneeName);

    private sealed record CommentRow(
        Guid TaskId,
        DateTimeOffset CreatedAt,
        string Author,
        string Content);
}
