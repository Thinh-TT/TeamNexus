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
/// <param name="Skills">
/// Giai đoạn 14.7 §A2 — canonical skill slugs. Read by the dispatching context builder so Jev can match a
/// task against a real skill set instead of guessing from a display name.
/// </param>
/// <param name="PrimaryRole">Giai đoạn 14.7 §A2 — <c>Tech Lead</c>, <c>Backend Dev</c>, … (<c>Member</c> by default).</param>
/// <param name="CapacityWeight">Giai đoạn 14.7 §A2 — relative capacity, 1.0 = full time.</param>
/// <param name="MaxWipLimit">Giai đoạn 14.7 §A2 — how many tasks may be in progress at once (0 = no limit).</param>
/// <remarks>
/// The first eight fields keep their exact names and order: <c>useWorkspaceMembers</c> and the
/// assignee dropdown (KanbanColumn, TaskDetailModal, Smart Setup) parse them. The Phase 14.7 capacity
/// fields are <b>appended</b> — the same append-only rule Phase 10 §4.2 used for
/// <c>WorkspaceSummaryResponse</c> and Phase 11 used for the fields above.
/// </remarks>
public sealed record WorkspaceMemberResponse(
    Guid UserId,
    string DisplayName,
    string Role,
    string? AvatarUrl,
    string MemberType,
    string? Email,
    DateTimeOffset JoinedAt,
    bool IsOwner,
    IReadOnlyList<string> Skills,
    string PrimaryRole,
    double CapacityWeight,
    int MaxWipLimit);

/// <summary>Phase 11 — change a member's workspace role (Admin only).</summary>
public sealed record UpdateMemberRoleRequest(string Role);
