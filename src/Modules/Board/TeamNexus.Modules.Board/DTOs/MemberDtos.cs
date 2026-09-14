namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// A workspace member as returned by GET /api/workspaces/{workspaceId}/members
/// (Phase 3 §3.1). Feeds the assignee dropdown when editing an AI proposal (§5) and is
/// reused by the Accountability Layer (Phase 4), the AI Observer (Phase 5) and the
/// AI Agent Executor (Phase 7).
/// </summary>
/// <param name="Role">Workspace role as text: <c>Admin</c> | <c>Manager</c> | <c>Member</c>.</param>
/// <param name="MemberType">
/// Phase 7 §3.6 — <c>"human"</c> | <c>"ai_agent"</c> (lowercase, matching DB design §4 and the
/// frontend contract). The AI Agent row is created lazily and sorts last (OrderBy JoinedAt).
/// </param>
/// <param name="Email">
/// Phase 11 — the member's account email. Visible to Member+ (every teammate is already identifiable
/// through the assignee dropdown and comment authors); only the member-management UI uses it.
/// </param>
/// <param name="JoinedAt">Phase 11 — when the membership was created.</param>
/// <param name="IsOwner">
/// Phase 11 — true for <c>workspaces.owner_id</c>. The UI disables "kick"/"đổi role" for this row
/// instead of letting the API answer 400.
/// </param>
/// <remarks>
/// The first five fields keep their exact names and order: <c>useWorkspaceMembers</c> and the
/// assignee dropdown (KanbanColumn, TaskDetailModal, Smart Setup) parse them. Phase 11 fields are
/// <b>appended</b> — the same append-only rule Phase 10 §4.2 used for
/// <c>WorkspaceSummaryResponse</c>.
/// </remarks>
public sealed record WorkspaceMemberResponse(
    Guid UserId,
    string DisplayName,
    string Role,
    string? AvatarUrl,
    string MemberType,
    string? Email,
    DateTimeOffset JoinedAt,
    bool IsOwner);

/// <summary>Phase 11 — change a member's workspace role (Admin only).</summary>
public sealed record UpdateMemberRoleRequest(string Role);
