namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Delivery outcome of one transactional email (Phase 11). Stored as text + CHECK
/// <c>ck_email_messages_status</c>.
/// </summary>
public enum EmailMessageStatus
{
    Queued,
    Sent,
    Failed,
}

/// <summary>
/// Audit log of every transactional email the API sends (Phase 11) — workspace invitations and
/// "quick email" messages composed by a Manager. Table: <c>email_messages</c>.
/// <para>
/// <b>Why this table exists:</b> the free-tier email quota (Resend, 3.000/month) is a real,
/// finite resource. The row is the counter used for the per-workspace rate limit and the evidence
/// that a specific address was actually handed to the provider — without it, a failed send is
/// invisible and an abuse loop is undetectable.
/// </para>
/// <para>
/// <b>Append-only, no soft delete, no query filter.</b> The log is evidence, not business content:
/// a workspace being soft-deleted must not erase the record that emails were sent in its name.
/// </para>
/// <para>
/// <b>Never stores the full body.</b> <see cref="BodyPreview"/> is a short, deliberately-composed
/// preview (≤ 500 chars) — in particular an invitation preview holds the subject + workspace name
/// and <b>never</b> the raw invitation token, which must not leak into a queryable column.
/// </para>
/// </summary>
public class EmailMessage : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public string ToEmail { get; set; } = string.Empty;

    /// <summary>Email kind from <c>EmailKinds</c>: <c>WorkspaceInvitation</c> | <c>QuickEmail</c>.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    /// <summary>Short preview of the body (≤ 500). Never the raw token, never the full body.</summary>
    public string BodyPreview { get; set; } = string.Empty;

    /// <summary>Sender for QuickEmail; the inviter for WorkspaceInvitation. Null when the user row is gone.</summary>
    public Guid? SentByUserId { get; set; }

    public EmailMessageStatus Status { get; set; } = EmailMessageStatus.Queued;

    /// <summary>Provider id returned by Resend; null until (and unless) the send succeeds.</summary>
    public string? ProviderMessageId { get; set; }

    /// <summary>Provider error message (≤ 500 chars); null when the send succeeded.</summary>
    public string? Error { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
