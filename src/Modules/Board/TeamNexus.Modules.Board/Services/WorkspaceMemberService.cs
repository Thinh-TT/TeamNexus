using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Reads and manages the members of a workspace (Phase 3 §3.1, extended in Phase 7 §3.6 and
/// Phase 11 §4.3).
/// <para>
/// Read is Member+; the two mutations are <b>Admin only</b>, per the roadmap wording
/// ("đổi role member (chỉ Admin), kick member (chỉ Admin)") — a Manager can invite and cancel
/// invitations but cannot repackage who has which role.
/// </para>
/// </summary>
public interface IWorkspaceMemberService
{
    /// <summary>Members of the workspace (Member+ only); 404 when it is not visible to the caller.</summary>
    Task<IReadOnlyList<WorkspaceMemberResponse>> GetMembersAsync(
        Guid workspaceId,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>Changes one member's workspace role (Admin only).</summary>
    Task UpdateMemberRoleAsync(
        Guid workspaceId,
        Guid memberUserId,
        UpdateMemberRoleRequest request,
        Guid userId,
        CancellationToken ct = default);

    /// <summary>Removes a member from the workspace (Admin only).</summary>
    Task RemoveMemberAsync(
        Guid workspaceId,
        Guid memberUserId,
        Guid userId,
        CancellationToken ct = default);
}

public sealed class WorkspaceMemberService : IWorkspaceMemberService
{
    private readonly TeamNexusDbContext _db;
    private readonly IWorkspaceAccess _access;
    private readonly IAiAgentResolver _agents;
    private readonly IActivityLogWriter _activityLog;
    private readonly ILogger<WorkspaceMemberService> _logger;

    public WorkspaceMemberService(
        TeamNexusDbContext db,
        IWorkspaceAccess access,
        IAiAgentResolver agents,
        IActivityLogWriter activityLog,
        ILogger<WorkspaceMemberService> logger)
    {
        _db = db;
        _access = access;
        _agents = agents;
        _activityLog = activityLog;
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

        // One extra scalar read for the owner id instead of joining the workspace into every row:
        // `IsOwner` is a per-response fact, not per-membership data.
        var ownerId = await _db.Workspaces
            .Where(w => w.Id == workspaceId)
            .Select(w => (Guid?)w.OwnerId)
            .FirstOrDefaultAsync(ct);

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
                MemberTypeToWire(wm.MemberType),
                wm.User.Email,
                wm.JoinedAt,
                ownerId is not null && ownerId.Value == wm.UserId))
            .ToList();
    }

    public async Task UpdateMemberRoleAsync(
        Guid workspaceId,
        Guid memberUserId,
        UpdateMemberRoleRequest request,
        Guid userId,
        CancellationToken ct = default)
    {
        var caller = await RequireAdminAsync(workspaceId, userId, ct);

        var role = ParseRole(request.Role);
        var target = await LoadTargetAsync(workspaceId, memberUserId, ct);
        var workspace = await _db.Workspaces.FirstAsync(w => w.Id == workspaceId, ct);

        GuardTarget(workspace, target, "đổi vai trò");

        if (target.Role == role)
        {
            return; // idempotent: re-sending the same role is not an error
        }

        // Losing the last Admin would leave the workspace without anyone able to change roles,
        // remove members or transfer ownership.
        if (target.Role == WorkspaceRole.Admin && role != WorkspaceRole.Admin)
        {
            await GuardLastAdminAsync(workspaceId, ct);
        }

        target.Role = role;
        await _db.SaveChangesAsync(ct);

        await _activityLog.RecordAsync(new ActivityLogEntry(
            workspaceId,
            BoardId: null,
            userId,
            ObserverEntityTypes.Workspace,
            target.UserId,
            ObserverActivityActions.MemberRoleChanged,
            JsonSerializer.Serialize(new { memberUserId = target.UserId, role = role.ToString() }, ActivityJson)),
            ct);
    }

    public async Task RemoveMemberAsync(
        Guid workspaceId,
        Guid memberUserId,
        Guid userId,
        CancellationToken ct = default)
    {
        var caller = await RequireAdminAsync(workspaceId, userId, ct);

        var target = await LoadTargetAsync(workspaceId, memberUserId, ct);
        var workspace = await _db.Workspaces.FirstAsync(w => w.Id == workspaceId, ct);

        GuardTarget(workspace, target, "xoá");

        // An Admin removing themselves is a request to leave, which has its own endpoint (and its
        // own rule about transferring ownership first).
        if (target.UserId == caller.UserId)
        {
            throw new BadRequestException("Hãy dùng chức năng \"Rời workspace\" để tự rời khỏi workspace.");
        }

        if (target.Role == WorkspaceRole.Admin)
        {
            await GuardLastAdminAsync(workspaceId, ct);
        }

        // workspace_members is a junction row, not a document: there is no soft delete to set and no
        // history to keep (the activity log already records who did what). Tasks assigned to this
        // member keep their assignee_id — the FK is Restrict, so nothing cascades and no work is
        // silently lost.
        _db.WorkspaceMembers.Remove(target);
        await _db.SaveChangesAsync(ct);

        await _activityLog.RecordAsync(new ActivityLogEntry(
            workspaceId,
            BoardId: null,
            userId,
            ObserverEntityTypes.Workspace,
            target.UserId,
            ObserverActivityActions.MemberRemoved,
            JsonSerializer.Serialize(new { memberUserId = target.UserId }, ActivityJson)),
            ct);
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>camelCase JSON for activity payloads (Phase 5 §2, decision A6).</summary>
    private static readonly JsonSerializerOptions ActivityJson = new(JsonSerializerDefaults.Web);

    private async Task<WorkspaceMember> RequireAdminAsync(
        Guid workspaceId, Guid userId, CancellationToken ct)
    {
        var membership = await _access.RequireMemberAsync(workspaceId, userId, ct);
        if (membership.Role != WorkspaceRole.Admin)
        {
            throw new ForbiddenException(
                "Chỉ Admin của workspace mới được đổi vai trò hoặc xoá thành viên.");
        }

        return membership;
    }

    private async Task<WorkspaceMember> LoadTargetAsync(
        Guid workspaceId, Guid memberUserId, CancellationToken ct)
        => await _db.WorkspaceMembers
            .FirstOrDefaultAsync(wm => wm.WorkspaceId == workspaceId && wm.UserId == memberUserId, ct)
            ?? throw new NotFoundException("Người này không phải thành viên của workspace.");

    /// <summary>
    /// The invariants shared by "change role" and "remove": an AI Agent is not manageable and the
    /// owner's row is not touchable, whatever the caller's role is.
    /// </summary>
    private static void GuardTarget(
        Workspace workspace, WorkspaceMember target, string operation)
    {
        if (target.MemberType == MemberType.AiAgent)
        {
            throw new BadRequestException(
                "Không thể thay đổi thành viên AI Agent (đây là trợ lý ảo của workspace).");
        }

        if (target.UserId == workspace.OwnerId)
        {
            throw new BadRequestException(
                $"Không thể {operation} chủ sở hữu workspace. Hãy chuyển quyền sở hữu trước.");
        }
    }

    /// <summary>Refuses when the target is the workspace's last remaining Admin.</summary>
    private async Task GuardLastAdminAsync(Guid workspaceId, CancellationToken ct)
    {
        var admins = await _db.WorkspaceMembers
            .CountAsync(wm => wm.WorkspaceId == workspaceId && wm.Role == WorkspaceRole.Admin, ct);

        if (admins <= 1)
        {
            throw new BadRequestException("Workspace phải còn ít nhất một Admin.");
        }
    }

    /// <summary>
    /// Strict role parse. <c>Enum.TryParse</c> alone is NOT a validator — it maps numeric strings by
    /// index ("1" ⇒ Manager) and would then violate <c>ck_workspace_members_role</c> for anything out
    /// of range. Same guard as <c>TaskService.ParsePriority</c> after Phase 10 BUG-1.
    /// </summary>
    public static WorkspaceRole ParseRole(string? role)
    {
        var value = role?.Trim();

        if (string.IsNullOrEmpty(value) || value.All(char.IsAsciiDigit)
            || !Enum.TryParse<WorkspaceRole>(value, ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            throw new BadRequestException("Vai trò không hợp lệ. Chỉ nhận Admin, Manager hoặc Member.");
        }

        return parsed;
    }

    /// <summary>
    /// Lowercase snake_case member type for the API ("human" | "ai_agent"), matching DB design §4
    /// and the frontend union type — not the CLR name that <c>ToString()</c> would produce.
    /// </summary>
    private static string MemberTypeToWire(MemberType memberType)
        => memberType == MemberType.AiAgent ? "ai_agent" : "human";
}
