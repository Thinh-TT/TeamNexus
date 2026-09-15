using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Board.Endpoints;

/// <summary>
/// Cross-board task search (Phase 12 §2) under
/// <c>/api/workspaces/{workspaceId}/tasks/search</c>.
/// <para>
/// <b>Why a separate route</b> instead of new query parameters on
/// <c>GET /api/boards/{boardId}/tasks</c>: that endpoint answers a bare <c>TaskResponse[]</c> and is
/// consumed by <c>useBoard</c>, <c>boardStore</c> and kanban drag-and-drop. A search needs a paged
/// envelope <i>and</i> a scope wider than one board — both would change a verified contract.
/// </para>
/// <para>
/// The handler is the <b>parser and validator</b> (query strings are strings until proven otherwise),
/// then hands a typed request to the service. Everything a user can type wrong becomes a 400 with a
/// message, surfaced through <see cref="DomainExceptionFilter"/>.
/// </para>
/// </summary>
public static class TaskSearchEndpoints
{
    /// <summary>Matches <c>tasks.title</c>/<c>tasks.description</c> limits — a longer query cannot match anything sensible.</summary>
    public const int MaxQueryLength = 200;

    /// <summary>Labels are ANDed; more than a handful is a UI mistake, not a search.</summary>
    public const int MaxLabelIds = 10;

    public const int DefaultTake = 25;

    public const int MaxTake = 100;

    public static IEndpointRouteBuilder MapTaskSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/workspaces")
            .WithTags("Search")
            .AddEndpointFilter<DomainExceptionFilter>();

        group.MapGet("/{workspaceId:guid}/tasks/search", SearchTasksAsync).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> SearchTasksAsync(
        Guid workspaceId,
        string? q,
        Guid? boardId,
        Guid? assigneeId,
        bool? unassigned,
        string? labelIds,
        string? priority,
        DateTimeOffset? dueFrom,
        DateTimeOffset? dueTo,
        bool? overdue,
        bool? includeDone,
        int? take,
        string? cursor,
        HttpContext http,
        ITaskSearchService search,
        CancellationToken ct)
    {
        var request = new TaskSearchRequest(
            WorkspaceId: workspaceId,
            Query: ParseQuery(q),
            BoardId: boardId,
            AssigneeId: assigneeId,
            Unassigned: unassigned ?? false,
            LabelIds: ParseLabelIds(labelIds),
            Priority: ParsePriority(priority),
            DueFrom: ToUtc(dueFrom),
            DueTo: ToUtc(dueTo),
            Overdue: overdue ?? false,
            IncludeDone: includeDone ?? true,
            Take: ClampTake(take),
            Cursor: cursor);

        var result = await search.SearchAsync(request, http.RequireUserId(), ct);
        return Results.Ok(result);
    }

    // ---- parsing & validation ----------------------------------------------

    /// <summary>Trims the text query; blank means "no text filter". Too long is a 400, not a silent truncation.</summary>
    private static string? ParseQuery(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > MaxQueryLength)
        {
            throw new BadRequestException($"Search text must be at most {MaxQueryLength} characters.");
        }

        return trimmed;
    }

    /// <summary>
    /// Parses the CSV of labels. Duplicates collapse (asking for the same label twice is not 11 labels);
    /// an unparsable id or more than <see cref="MaxLabelIds"/> is a 400.
    /// </summary>
    private static IReadOnlyList<Guid> ParseLabelIds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        var parts = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (parts.Count == 0)
        {
            return [];
        }

        if (parts.Count > MaxLabelIds)
        {
            throw new BadRequestException($"At most {MaxLabelIds} labels can be filtered at once.");
        }

        var ids = new List<Guid>(parts.Count);
        foreach (var part in parts)
        {
            if (!Guid.TryParse(part, out var id))
            {
                throw new BadRequestException("labelIds must be a comma-separated list of guids.");
            }

            ids.Add(id);
        }

        return ids;
    }

    /// <summary>
    /// Validates <c>priority</c> against the four stored names. <c>Enum.TryParse</c> alone is NOT a
    /// validator — it accepts "1" as the enum member at that index (Phase 10's BUG-1: <c>"1"</c>
    /// silently became <c>Medium</c>), so a numeric string is rejected explicitly and
    /// <c>Enum.IsDefined</c> closes the out-of-range hole that would otherwise reach the CHECK
    /// constraint as a 500.
    /// </summary>
    private static string ParsePriority(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        if (IsNumericString(trimmed)
            || !Enum.TryParse<TaskPriority>(trimmed, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new BadRequestException("Priority must be one of: Low, Medium, High, Urgent.");
        }

        return parsed.ToString();
    }

    /// <summary>
    /// True when the value is only a sign and digits — the shape <c>Enum.TryParse</c> reads as a
    /// numeric value rather than a member name. Deliberately narrow so <c>"Urgent"</c> is never
    /// mistaken for a number (mirrors <c>TaskService.IsNumericString</c>).
    /// </summary>
    private static bool IsNumericString(string value)
    {
        var digits = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (c is >= '0' and <= '9')
            {
                digits++;
                continue;
            }

            if (c is '-' or '+' && i == 0)
            {
                continue;
            }

            return false;
        }

        return digits > 0;
    }

    /// <summary>
    /// Npgsql refuses a <see cref="DateTimeOffset"/> whose offset is not zero when writing to
    /// <c>timestamptz</c>, and a client is entitled to send <c>2026-06-15T09:00:00+07:00</c>. The
    /// value is an instant, so normalizing preserves the meaning (Phase 10's due-date bug, guarded
    /// here so the search cannot reintroduce it on the read path).
    /// </summary>
    private static DateTimeOffset? ToUtc(DateTimeOffset? value)
        => value?.ToUniversalTime();

    /// <summary>
    /// Clamps the page size instead of rejecting it: <c>take</c> is a UI tuning knob, not client input
    /// worth a 400 (same rule as <c>WorkspaceActivityService.ClampTake</c>).
    /// </summary>
    private static int ClampTake(int? value)
        => value switch
        {
            null => DefaultTake,
            <= 0 => 1,
            > MaxTake => MaxTake,
            _ => value.Value,
        };
}
