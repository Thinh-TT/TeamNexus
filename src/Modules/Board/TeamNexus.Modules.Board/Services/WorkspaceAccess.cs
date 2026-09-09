using Microsoft.EntityFrameworkCore;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Resolves workspace-level authorization for Board operations. Uses the per-workspace
/// membership role (workspace_members.role — DB design §3.3 "second authorization layer")
/// as the single source of truth; global Identity roles are not consulted here.
/// </summary>
public interface IWorkspaceAccess
{
    /// <summary>Returns the caller's membership, throwing 404 when the workspace is not visible to them.</summary>
    Task<WorkspaceMember> RequireMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>Membership AND role Manager/Admin; throws 403 for plain members.</summary>
    Task<WorkspaceMember> RequireManagerAsync(Guid workspaceId, Guid userId, CancellationToken ct = default);

    /// <summary>Checks that the given membership role is at least the required one.</summary>
    static bool IsAtLeast(WorkspaceRole actual, WorkspaceRole required) => actual switch
    {
        WorkspaceRole.Admin => true,
        WorkspaceRole.Manager => required is WorkspaceRole.Manager or WorkspaceRole.Member,
        _ => required == WorkspaceRole.Member,
    };
}

public sealed class WorkspaceAccess : IWorkspaceAccess
{
    private readonly TeamNexusDbContext _db;

    public WorkspaceAccess(TeamNexusDbContext db)
    {
        _db = db;
    }

    public async Task<WorkspaceMember> RequireMemberAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        var membership = await FindAsync(workspaceId, userId, ct);
        if (membership is null)
        {
            // Do not reveal whether the workspace exists.
            throw new NotFoundException("Workspace not found or you are not a member.");
        }

        return membership;
    }

    public async Task<WorkspaceMember> RequireManagerAsync(Guid workspaceId, Guid userId, CancellationToken ct = default)
    {
        var membership = await RequireMemberAsync(workspaceId, userId, ct);
        if (!IWorkspaceAccess.IsAtLeast(membership.Role, WorkspaceRole.Manager))
        {
            throw new ForbiddenException("Requires Manager or Admin role in this workspace.");
        }

        return membership;
    }

    private Task<WorkspaceMember?> FindAsync(Guid workspaceId, Guid userId, CancellationToken ct)
        => _db.WorkspaceMembers
            .FirstOrDefaultAsync(wm => wm.WorkspaceId == workspaceId && wm.UserId == userId, ct);
}
