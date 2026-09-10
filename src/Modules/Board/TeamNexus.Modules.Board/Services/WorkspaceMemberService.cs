using Microsoft.EntityFrameworkCore;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Reads the members of a workspace (Phase 3 §3.1). Read-only: this service never writes.
/// </summary>
public interface IWorkspaceMemberService
{
    /// <summary>Members of the workspace (Member+ only); 404 when it is not visible to the caller.</summary>
    Task<IReadOnlyList<WorkspaceMemberResponse>> GetMembersAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default);
}

public sealed class WorkspaceMemberService : IWorkspaceMemberService
{
    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;

    public WorkspaceMemberService(TeamNexusDbContext db, IWorkspaceAccess access)
    {
        _db = db;
        _access = access;
    }

    public async Task<IReadOnlyList<WorkspaceMemberResponse>> GetMembersAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default)
    {
        // Throws NotFoundException (404) when the workspace is missing or not visible.
        await _access.RequireMemberAsync(workspaceId, userId, ct);

        // The WorkspaceMember global query filter already hides memberships of a soft-deleted
        // workspace (WorkspaceMemberConfiguration). Include(User) is safe here because
        // ApplicationUser has no query filter — the FK is Restrict, so a User row can only be
        // absent if it was hard-deleted, in which case the Include simply yields null and the
        // entry is filtered out below.
        var members = await _db.WorkspaceMembers
            .Where(wm => wm.WorkspaceId == workspaceId)
            .Include(wm => wm.User)
            .OrderBy(wm => wm.JoinedAt)
            .AsNoTracking()
            .ToListAsync(ct);

        return members
            .Where(wm => wm.User is not null)
            .Select(wm => new WorkspaceMemberResponse(
                wm.UserId,
                wm.User!.DisplayName,
                wm.Role.ToString(),
                wm.User.AvatarUrl))
            .ToList();
    }
}
