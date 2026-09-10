namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// A workspace member as returned by GET /api/workspaces/{workspaceId}/members
/// (Phase 3 §3.1). Feeds the assignee dropdown when editing an AI proposal (§5) and is
/// reused by the Accountability Layer (Phase 4) and the AI Observer (Phase 5).
/// </summary>
/// <param name="Role">Workspace role as text: <c>Admin</c> | <c>Manager</c> | <c>Member</c>.</param>
public sealed record WorkspaceMemberResponse(
    Guid UserId,
    string DisplayName,
    string Role,
    string? AvatarUrl);
