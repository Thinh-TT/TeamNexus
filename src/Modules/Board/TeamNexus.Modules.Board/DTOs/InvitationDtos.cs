namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// A workspace invitation as returned by
/// <c>GET|POST /api/workspaces/{workspaceId}/invitations</c> (Phase 11).
/// <para>
/// <b>The raw token is never part of this shape.</b> It exists only inside the email body; the API
/// exposes the invitation's identity, its target and its lifecycle, nothing that could be replayed.
/// </para>
/// <param name="InvitedRole">Workspace role granted on accept: <c>Admin</c> | <c>Manager</c> | <c>Member</c>.</param>
/// <param name="Status"><c>Pending</c> | <c>Accepted</c> | <c>Cancelled</c> | <c>Expired</c>.</param>
public sealed record InvitationResponse(
    Guid Id,
    string InvitedEmail,
    string InvitedRole,
    string Status,
    string InvitedByName,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    Guid? AcceptedByUserId,
    DateTimeOffset? AcceptedAt,
    /// <summary>
    /// Phase 11 — appended last so the earlier fields keep their positions.
    /// False when the invitation was stored but the provider rejected/never received the email:
    /// the row is real business data, so the API still reports success and the UI offers "Gửi lại".
    /// </summary>
    bool EmailSent);

/// <summary>
/// Request to invite one email address. <c>Role</c> null/blank means <c>Member</c>
/// (the roadmap default) — the recipient can never choose it themselves.
/// </summary>
public sealed record CreateInvitationRequest(string Email, string? Role);

/// <summary>
/// Anonymous preview of an invitation token, so the accept page can show what the visitor is about
/// to join <b>before</b> they sign in.
/// <para>
/// Deliberately minimal: workspace name, inviter name, target email, role and expiry. It never
/// discloses member lists, boards or any id other than the workspace being joined.
/// </para>
/// </summary>
public sealed record InvitationPreviewResponse(
    Guid WorkspaceId,
    string WorkspaceName,
    string InvitedEmail,
    string InvitedRole,
    string InvitedByName,
    DateTimeOffset ExpiresAt,
    string Status);

/// <summary>
/// Result of accepting an invitation. <paramref name="Role"/> is the role the caller actually holds
/// afterwards — which is their existing role when they were already a member
/// (<paramref name="AlreadyMember"/> is true), never the invitation's role overwriting it.
/// </summary>
public sealed record AcceptInvitationResponse(Guid WorkspaceId, string Role, bool AlreadyMember);
