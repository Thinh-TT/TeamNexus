using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Reads the members of a workspace (Phase 3 §3.1, extended in Phase 7 §3.6).
/// Read-only for membership data: it never writes workspace_members — the only side effect is the
/// lazy creation of the AI Agent pseudo-member explained on <see cref="GetMembersAsync"/>.
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
    private readonly IAiAgentResolver _agents;
    private readonly ILogger<WorkspaceMemberService> _logger;

    public WorkspaceMemberService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IAiAgentResolver agents,
        ILogger<WorkspaceMemberService> logger)
    {
        _db = db;
        _access = access;
        _agents = agents;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WorkspaceMemberResponse>> GetMembersAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default)
    {
        // Throws NotFoundException (404) when the workspace is missing or not visible.
        await _access.RequireMemberAsync(workspaceId, userId, ct);

        // Phase 7 §3.6 (decision D1): the AI Agent is a real users row created lazily. This list is
        // the SOLE source of the assignee dropdown, so the agent row must exist before we read —
        // otherwise no workspace could ever assign work to the agent, which is the whole point of
        // Phase 7. Best-effort by design: failing to create the agent must not break the members
        // endpoint (same fail-soft rule as IActivityLogWriter / IBoardEventPublisher).
        try
        {
            await _agents.EnsureAgentAsync(workspaceId, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not ensure the AI Agent member for workspace {WorkspaceId}; "
                + "returning the members without an agent.",
                workspaceId);
        }

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

        // OrderBy(JoinedAt) keeps the agent last (it is always created after the humans), which is
        // the desired position in the dropdown. Agents are deliberately NOT filtered out here.
        return members
            .Where(wm => wm.User is not null)
            .Select(wm => new WorkspaceMemberResponse(
                wm.UserId,
                wm.User!.DisplayName,
                wm.Role.ToString(),
                wm.User.AvatarUrl,
                MemberTypeToWire(wm.MemberType)))
            .ToList();
    }

    /// <summary>
    /// Lowercase snake_case member type for the API ("human" | "ai_agent"), matching DB design §4
    /// and the frontend union type — not the CLR name that <c>ToString()</c> would produce.
    /// </summary>
    private static string MemberTypeToWire(MemberType memberType)
        => memberType == MemberType.AiAgent ? "ai_agent" : "human";
}
