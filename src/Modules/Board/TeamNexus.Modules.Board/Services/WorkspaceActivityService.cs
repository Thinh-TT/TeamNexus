using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Reads the workspace activity feed for the Activity Log UI (Phase 10 §2.4, decision D6).
/// <para>
/// <b>Why keyset and not <c>Skip</c>/<c>Take</c>:</b> <c>activity_logs</c> is an append-only table
/// whose only index is <c>(workspace_id, created_at)</c>. <c>OFFSET</c> would force PostgreSQL to
/// walk and discard every skipped row, and the feed grows for the lifetime of the workspace. A
/// cursor over <c>(created_at, id)</c> uses that index directly and — because rows keep being
/// appended — never shifts underneath a client that is paging (no duplicated or skipped entries).
/// </para>
/// <para>
/// Manager+ only, matching the roadmap ("Manager/Admin"). The log contains board-wide history
/// including other people's activity, so it is not a member-level read.
/// </para>
/// </summary>
public interface IWorkspaceActivityService
{
    Task<WorkspaceActivityPageResponse> GetAsync(
        Guid workspaceId,
        Guid userId,
        Guid? boardId,
        string? entityType,
        string? action,
        int? take,
        string? before,
        CancellationToken ct = default);
}

public sealed class WorkspaceActivityService : IWorkspaceActivityService
{
    /// <summary>Page size when the caller does not ask for one.</summary>
    public const int DefaultTake = 50;

    /// <summary>Upper bound on a single page — the feed is rendered as a list, not streamed.</summary>
    public const int MaxTake = 200;

    /// <summary>Separates the two cursor components; never appears in an ISO-8601 timestamp.</summary>
    private const char CursorSeparator = '|';

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;

    public WorkspaceActivityService(TeamNexusDbContext db, IWorkspaceAccess access)
    {
        _db = db;
        _access = access;
    }

    public async Task<WorkspaceActivityPageResponse> GetAsync(
        Guid workspaceId,
        Guid userId,
        Guid? boardId,
        string? entityType,
        string? action,
        int? take,
        string? before,
        CancellationToken ct = default)
    {
        // 404 for a non-member, 403 for a plain member.
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var pageSize = ClampTake(take);
        var cursor = ParseCursor(before);

        var query = _db.Activities
            .AsNoTracking()
            .Where(a => a.WorkspaceId == workspaceId);

        if (boardId.HasValue)
        {
            // Workspace-level rows carry board_id = NULL and belong to no board, so they are
            // correctly excluded when a single board is selected.
            query = query.Where(a => a.BoardId == boardId.Value);
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(a => a.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(a => a.Action == action);
        }

        if (cursor is { } c)
        {
            // Strictly older than the cursor. The Id tiebreaker keeps rows that share a created_at
            // (very likely: one request can write several rows) from being skipped or repeated.
            //
            // Comparison operators, NOT Guid.CompareTo: Npgsql maps uuid comparison to the server's
            // byte-wise ordering, and EF translates `<`/`>` here without a client-side evaluation.
            query = query.Where(a =>
                a.CreatedAt < c.CreatedAt
                || (a.CreatedAt == c.CreatedAt && a.Id < c.Id));
        }

        // Actor display name via a LEFT JOIN: ActivityLog intentionally declares its FK without a
        // navigation property, so Include() is not available here.
        var rows = await query
            .LeftJoin(
                _db.Users,
                a => a.UserId,
                u => (Guid?)u.Id,
                (a, u) => new
                {
                    Activity = a,
                    AuthorName = u == null ? null : u.DisplayName,
                })
            .OrderByDescending(x => x.Activity.CreatedAt)
            .ThenByDescending(x => x.Activity.Id)
            .Take(pageSize + 1)   // one extra row answers HasMore without a COUNT
            .ToListAsync(ct);

        var hasMore = rows.Count > pageSize;
        var page = hasMore ? rows.Take(pageSize).ToList() : rows;

        var items = page
            .Select(x => new WorkspaceActivityItemResponse(
                x.Activity.Id,
                x.Activity.BoardId,
                x.Activity.UserId,
                x.AuthorName,
                x.Activity.EntityType,
                x.Activity.EntityId,
                x.Activity.Action,
                DeserializePayload(x.Activity.Payload),
                x.Activity.CreatedAt))
            .ToList();

        return new WorkspaceActivityPageResponse(
            items,
            hasMore && page.Count > 0 ? FormatCursor(page[^1].Activity.CreatedAt, page[^1].Activity.Id) : null,
            hasMore);
    }

    // ---- helpers ----------------------------------------------------------

    /// <summary>
    /// Clamps the requested page size into <c>[1, <see cref="MaxTake"/>]</c>. An out-of-range value
    /// is clamped rather than rejected: it is a UI tuning knob, not client input worth a 400.
    /// </summary>
    private static int ClampTake(int? take)
        => take switch
        {
            null => DefaultTake,
            <= 0 => 1,
            > MaxTake => MaxTake,
            _ => take.Value,
        };

    private static string FormatCursor(DateTimeOffset createdAt, Guid id)
        => $"{createdAt:O}{CursorSeparator}{id:D}";

    /// <summary>
    /// Parses <c>"{createdAt:O}|{id:D}"</c>. A malformed cursor is a 400 with a clear message —
    /// never a 500, and never a silent "start from the beginning" (which would loop the UI forever).
    /// </summary>
    private static (DateTimeOffset CreatedAt, Guid Id)? ParseCursor(string? before)
    {
        if (string.IsNullOrWhiteSpace(before))
        {
            return null;
        }

        var separator = before.IndexOf(CursorSeparator);
        if (separator <= 0 || separator == before.Length - 1)
        {
            throw new BadRequestException(
                "Invalid cursor: expected '<ISO-8601 timestamp>|<guid>'.");
        }

        var timestamp = before[..separator];
        var id = before[(separator + 1)..];

        if (!DateTimeOffset.TryParse(
                timestamp,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var createdAt)
            || !Guid.TryParse(id, out var entityId))
        {
            throw new BadRequestException(
                "Invalid cursor: expected '<ISO-8601 timestamp>|<guid>'.");
        }

        return (createdAt, entityId);
    }

    /// <summary>
    /// Turns the raw <c>jsonb</c> text into a <see cref="JsonElement"/> so it can be forwarded
    /// verbatim. A row whose payload is missing or not valid JSON yields <c>null</c> instead of
    /// failing the whole page — the log is append-only history and one bad row must not hide the rest.
    /// </summary>
    private static JsonElement? DeserializePayload(string? payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
