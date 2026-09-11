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
public sealed record WorkspaceMemberResponse(
    Guid UserId,
    string DisplayName,
    string Role,
    string? AvatarUrl,
    string MemberType);
