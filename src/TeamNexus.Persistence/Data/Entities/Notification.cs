namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// In-app alert produced by the AI Observer and addressed to a <b>single</b> recipient
/// (Manager/Admin of the workspace) — never public to the whole team (Phase 5 §1 / DB design §3.6).
/// Table: <c>notifications</c>.
/// <para>
/// Fan-out model: one row per recipient, so <c>is_read</c>/<c>read_at</c> are per person and
/// "my unread alerts" is a single indexed lookup on <c>(recipient_user_id, is_read)</c> with no
/// join table. No soft delete: alert history survives a Manager leaving the workspace; read
/// access is filtered by <c>recipient_user_id</c>.
/// </para>
/// </summary>
public class Notification : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Workspace the alert belongs to.</summary>
    public Guid WorkspaceId { get; set; }

    /// <summary>Manager/Admin receiving this alert (one row per recipient).</summary>
    public Guid RecipientUserId { get; set; }

    /// <summary>Alert type from <c>NotificationTypes</c> (free text — DB design §4).</summary>
    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    /// <summary>jsonb — alert detail: runId, boardId, taskIds, userIds, severity, model, tokens.</summary>
    public string? Payload { get; set; }

    /// <summary>
    /// Per-recipient read flag. Initialised in the entity (no DB default) — same reasoning as
    /// <c>AiActionLog.Status</c> (Phase 4 §1.2).
    /// </summary>
    public bool IsRead { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? ReadAt { get; set; }

    public ApplicationUser? RecipientUser { get; set; }
}
