namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Lifecycle of a workspace invitation (Phase 11 / DB design §3.3).
/// Stored as text + CHECK <c>ck_workspace_invitations_status</c> — the value set is fixed, so it
/// follows the same rule as <c>WorkspaceRole</c>/<c>AiActionStatus</c> rather than the free-text
/// convention used for <c>notifications.type</c>.
/// </summary>
public enum InvitationStatus
{
    Pending,
    Accepted,
    Cancelled,
    Expired,
}

/// <summary>
/// An email invitation to join a workspace (Phase 11). A Manager/Admin creates a <c>Pending</c>
/// row; the recipient clicks the emailed link to <c>Accept</c> it; a link that is cancelled or
/// past <see cref="ExpiresAt"/> becomes <c>Cancelled</c>/<c>Expired</c>.
/// Table: <c>workspace_invitations</c> (DB design §3.3).
/// <para>
/// <b>The raw token is never stored.</b> Only its SHA-256 hash lives in
/// <see cref="TokenHash"/> — the same shape as <c>refresh_tokens.token_hash</c> (Phase 1). The raw
/// value exists exactly once: inside the email body.
/// </para>
/// <para>
/// A partial unique index (<c>uq_workspace_invitations_pending</c>, on
/// <c>(workspace_id, invited_email) WHERE status = 'Pending'</c>) guarantees at most one live
/// invitation per email per workspace, so re-inviting after a cancel/expiry is allowed but never
/// queues two pending links for the same person.
/// </para>
/// </summary>
public class WorkspaceInvitation : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    /// <summary>
    /// Invitee email, always stored <b>normalized</b> (trimmed + lowercased) so the partial unique
    /// index and the accept-time "does this email match?" check agree on one canonical form.
    /// </summary>
    public string InvitedEmail { get; set; } = string.Empty;

    /// <summary>
    /// Role granted on accept. Chosen by the inviter (default <see cref="WorkspaceRole.Member"/>) —
    /// the recipient can never pick it, otherwise they could promote themselves to Admin.
    /// </summary>
    public WorkspaceRole InvitedRole { get; set; } = WorkspaceRole.Member;

    /// <summary>SHA-256 of the raw token sent by email (never the token itself).</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Manager/Admin who created the invitation.</summary>
    public Guid InvitedByUserId { get; set; }

    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>Set when the recipient accepts (an existing user or one who just signed up).</summary>
    public Guid? AcceptedByUserId { get; set; }

    public DateTimeOffset? AcceptedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Workspace? Workspace { get; set; }

    public ApplicationUser? InvitedBy { get; set; }
}
