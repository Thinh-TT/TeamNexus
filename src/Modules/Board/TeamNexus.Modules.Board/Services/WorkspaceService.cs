using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Workspace metadata and ownership (Phase 10 §2.2, decisions D1–D5).
/// <para>
/// <b>Where this came from:</b> the read path was an inline minimal-API handler inside
/// <c>Program.cs</c> (lines 94–141) since Giai đoạn 1. Phase 10 moved it here so the entry point
/// stops holding business logic, and added the mutations the roadmap's "Workspace Settings" box
/// needs (rename, transfer ownership, delete) — none of which replace a schema change.
/// </para>
/// <para>
/// Every method authorizes through <see cref="IWorkspaceAccess"/>: a non-member gets 404 (never
/// 403, so workspace existence cannot be probed) and a plain member gets 403 where a manager is
/// required.
/// </para>
/// </summary>
public interface IWorkspaceService
{
    /// <summary>
    /// Workspaces the caller belongs to, most recently updated first. Lazily creates the caller's
    /// default workspace when they have none — see the note on
    /// <see cref="WorkspaceService.ListForUserAsync"/>.
    /// </summary>
    Task<IReadOnlyList<WorkspaceSummaryResponse>> ListForUserAsync(Guid userId, CancellationToken ct = default);

    Task<WorkspaceDetailResponse> GetAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>Manager+ only. Validates the name (1–120 characters) and records activity.</summary>
    Task UpdateAsync(Guid workspaceId, UpdateWorkspaceRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Owner or workspace Admin only. Promotes the new owner to Admin; the old owner keeps their role.</summary>
    Task TransferOwnershipAsync(
        Guid workspaceId, TransferOwnershipRequest request, Guid userId, CancellationToken ct = default);

    /// <summary>Owner or workspace Admin only. Soft delete.</summary>
    Task DeleteAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);
}

public sealed class WorkspaceService : IWorkspaceService
{
    /// <summary>Matches <c>workspaces.name</c> varchar(120) and <c>BoardService.ValidateBoardName</c>.</summary>
    private const int MaxNameLength = 120;

    private const string OwnerUnknown = "Không xác định";

    /// <summary>camelCase JSON for activity payloads (Phase 5 §2, decision A6).</summary>
    private static readonly JsonSerializerOptions ActivityJson = new(JsonSerializerDefaults.Web);

    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IActivityLogWriter _activityLog;

    public WorkspaceService(TeamNexusDbContext db, IWorkspaceAccess access, IActivityLogWriter activityLog)
    {
        _db = db;
        _access = access;
        _activityLog = activityLog;
    }

    public async Task<IReadOnlyList<WorkspaceSummaryResponse>> ListForUserAsync(
        Guid userId, CancellationToken ct = default)
    {
        var memberships = await LoadMembershipsAsync(userId, ct);

        if (memberships.Count == 0)
        {
            memberships = [await EnsureDefaultWorkspaceAsync(userId, ct)];
        }

        return memberships
            .Where(wm => wm.Workspace is not null)
            .Select(wm => new WorkspaceSummaryResponse(
                wm.WorkspaceId,
                wm.Workspace!.Name,
                wm.Workspace.Description,
                wm.Role.ToString(),
                wm.Workspace.OwnerId,
                // isOwner is false rather than throwing when the row has a stale owner id.
                wm.Workspace.OwnerId == userId))
            .ToList();
    }

    public async Task<WorkspaceDetailResponse> GetAsync(
        Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        var workspace = await RequireVisibleWorkspaceAsync(workspaceId, ct);
        var membership = await _access.RequireMemberAsync(workspaceId, userId, ct);

        // Three scalar queries, never a per-row loop (no N+1).
        var memberCount = await _db.WorkspaceMembers.CountAsync(wm => wm.WorkspaceId == workspaceId, ct);
        var boardCount = await _db.Boards.CountAsync(b => b.WorkspaceId == workspaceId, ct);
        var ownerName = await _db.Users
            .Where(u => u.Id == workspace.OwnerId)
            .Select(u => u.DisplayName)
            .FirstOrDefaultAsync(ct);

        return new WorkspaceDetailResponse(
            workspace.Id,
            workspace.Name,
            workspace.Description,
            workspace.CreatedAt,
            workspace.UpdatedAt,
            workspace.OwnerId,
            string.IsNullOrWhiteSpace(ownerName) ? OwnerUnknown : ownerName,
            memberCount,
            boardCount,
            membership.Role.ToString());
    }

    public async Task UpdateAsync(
        Guid workspaceId, UpdateWorkspaceRequest request, Guid userId, CancellationToken ct = default)
    {
        var workspace = await RequireVisibleWorkspaceAsync(workspaceId, ct);
        await _access.RequireManagerAsync(workspaceId, userId, ct);

        var name = RequireValidName(request.Name);
        var description = TrimToNull(request.Description);

        // Captured before mutating: the payload reports whether anything actually changed rather
        // than storing the (potentially long) description text — same rule as Phase 5 §2.3.
        var nameChanged = !string.Equals(workspace.Name, name, StringComparison.Ordinal);
        var descriptionChanged = !string.Equals(workspace.Description, description, StringComparison.Ordinal);

        workspace.Name = name;
        workspace.Description = description;

        await _db.SaveChangesAsync(ct);

        if (nameChanged || descriptionChanged)
        {
            await RecordWorkspaceActivityAsync(
                workspaceId,
                userId,
                ObserverActivityActions.WorkspaceUpdated,
                new { nameChanged, descriptionChanged, ownerId = workspace.OwnerId },
                ct);
        }
    }

    public async Task TransferOwnershipAsync(
        Guid workspaceId, TransferOwnershipRequest request, Guid userId, CancellationToken ct = default)
    {
        var workspace = await RequireVisibleWorkspaceAsync(workspaceId, ct);

        // Manager is the minimum bar, then the stricter owner/Admin gate is applied on top: a
        // Manager who does not own the workspace must not be able to hand it away (Phase 10 D2).
        var membership = await _access.RequireManagerAsync(workspaceId, userId, ct);
        if (workspace.OwnerId != userId && membership.Role != WorkspaceRole.Admin)
        {
            throw new ForbiddenException("Only the workspace owner or an Admin can transfer ownership.");
        }

        if (request.NewOwnerId == workspace.OwnerId)
        {
            throw new BadRequestException("The new owner must be different from the current owner.");
        }

        var newOwner = await _db.WorkspaceMembers
            .FirstOrDefaultAsync(wm => wm.WorkspaceId == workspaceId && wm.UserId == request.NewOwnerId, ct)
            ?? throw new BadRequestException("The new owner must be a member of this workspace.");

        // The AI Agent is a pseudo-member: it can be assigned work, but it cannot own a workspace.
        if (newOwner.MemberType == MemberType.AiAgent)
        {
            throw new BadRequestException("Ownership cannot be transferred to the AI Agent.");
        }

        var previousOwnerId = workspace.OwnerId;

        await using var transaction = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            workspace.OwnerId = newOwner.UserId;

            // Promote the new owner (the old owner keeps whatever role they had — D3).
            if (newOwner.Role != WorkspaceRole.Admin)
            {
                newOwner.Role = WorkspaceRole.Admin;
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch
        {
            // Roll the transaction back AND detach the pending edits: a failed transfer must not
            // leave a modified entity in the tracked context, where a later SaveChangesAsync in the
            // same request scope would silently apply half of the change.
            await transaction.RollbackAsync(CancellationToken.None);
            _db.ChangeTracker.Clear();
            throw;
        }

        await RecordWorkspaceActivityAsync(
            workspaceId,
            userId,
            ObserverActivityActions.WorkspaceOwnerTransferred,
            new { previousOwnerId, newOwnerId = newOwner.UserId },
            ct);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        var workspace = await RequireVisibleWorkspaceAsync(workspaceId, ct);

        var membership = await _access.RequireManagerAsync(workspaceId, userId, ct);
        if (workspace.OwnerId != userId && membership.Role != WorkspaceRole.Admin)
        {
            throw new ForbiddenException("Only the workspace owner or an Admin can delete this workspace.");
        }

        var name = workspace.Name;

        // Soft delete (D4). The global query filter on Workspace hides it from every read from here
        // on, and the memberships/boards/tasks follow through their own filters — nothing is
        // hard-deleted, so history (and the activity log) survives in the database.
        workspace.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        // Recorded after the write: the log must reflect what actually happened, and IActivityLogWriter
        // inserts a plain row (no navigation), so a now-hidden workspace can still be logged.
        await RecordWorkspaceActivityAsync(
            workspaceId,
            userId,
            ObserverActivityActions.WorkspaceDeleted,
            new { name },
            ct);
    }

    // ---- helpers ----------------------------------------------------------

    /// <summary>
    /// Memberships with their workspace loaded, in a stable order (oldest first). The previous
    /// inline handler returned whatever order the database happened to produce; <c>DashboardPage</c>
    /// takes the first entry, so a deterministic order matters for that screen.
    /// </summary>
    private Task<List<WorkspaceMember>> LoadMembershipsAsync(Guid userId, CancellationToken ct)
        => _db.WorkspaceMembers
            .Include(wm => wm.Workspace)
            .Where(wm => wm.UserId == userId && wm.Workspace != null)
            .OrderBy(wm => wm.Workspace!.CreatedAt)
            .ThenBy(wm => wm.WorkspaceId)
            .ToListAsync(ct);

    /// <summary>
    /// Creates the caller's default workspace, exactly as the Giai đoạn 1 inline handler did
    /// (same Vietnamese strings and Admin role) — <c>DashboardPage</c> and the board list rely on
    /// every user always having at least one workspace to land in.
    /// <para>
    /// <b>Known race:</b> two concurrent first requests can each create one workspace, because
    /// <c>workspace_members</c> has no uniqueness rule that would prevent it. The old handler had the
    /// same behaviour; Phase 11 (member management) is the natural place to close it.
    /// </para>
    /// </summary>
    private async Task<WorkspaceMember> EnsureDefaultWorkspaceAsync(Guid userId, CancellationToken ct)
    {
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(),
            Name = "Không Gian Làm Việc Chính",
            Description = "Workspace mặc định để quản lý bảng Kanban",
            OwnerId = userId,
        };

        var membership = new WorkspaceMember
        {
            WorkspaceId = workspace.Id,
            UserId = userId,
            Role = WorkspaceRole.Admin,
            Workspace = workspace,
        };

        _db.Workspaces.Add(workspace);
        _db.WorkspaceMembers.Add(membership);
        await _db.SaveChangesAsync(ct);

        return membership;
    }

    /// <summary>
    /// Loads a workspace that is visible to anyone at all (the query filter already hides
    /// soft-deleted rows) so the caller can be authorized against its membership afterwards.
    /// </summary>
    private async Task<Workspace> RequireVisibleWorkspaceAsync(Guid workspaceId, CancellationToken ct)
        => await _db.Workspaces.FirstOrDefaultAsync(w => w.Id == workspaceId, ct)
            ?? throw new NotFoundException("Workspace not found or you are not a member.");

    private static string RequireValidName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length == 0 || trimmed.Length > MaxNameLength)
        {
            throw new BadRequestException($"Workspace name must be 1–{MaxNameLength} characters.");
        }

        return trimmed;
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    /// <summary>
    /// Records one workspace-level activity row (<c>board_id = NULL</c>, <c>entity_id</c> = the
    /// workspace). Best-effort by contract: <see cref="IActivityLogWriter"/> never throws, so a
    /// logging failure can never fail the mutation the caller asked for.
    /// </summary>
    private Task RecordWorkspaceActivityAsync(
        Guid workspaceId, Guid userId, string action, object payload, CancellationToken ct)
        => _activityLog.RecordAsync(
            new ActivityLogEntry(
                workspaceId,
                BoardId: null,
                userId,
                ObserverEntityTypes.Workspace,
                workspaceId,
                action,
                JsonSerializer.Serialize(payload, ActivityJson)),
            ct);
}
