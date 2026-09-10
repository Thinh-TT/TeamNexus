namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Lifecycle of an AI write action (Accountability Layer, Phase 4 §1). Stored as text +
/// CHECK constraint <c>ck_ai_action_logs_status</c> (DB design §3.5/§4).
/// </summary>
public enum AiActionStatus
{
    Pending,
    Approved,
    Rejected,
    Undone,
}

/// <summary>
/// Audit trail of every AI action that writes data — the row is created before any real
/// data is written (Pending), and only an Approved action is applied. Table:
/// <c>ai_action_logs</c> (DB design §3.5).
/// <para>
/// Immutable history: no soft delete and deliberately <b>no</b> global query filter, so the
/// trail stays readable even after the affected board is soft-deleted.
/// </para>
/// </summary>
public class AiActionLog : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Action type from <c>AiActionTypes</c> (module Ai), e.g. <c>CreateSubtasks</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Affected entity type from <c>AiEntityTypes</c>; <c>Board</c> for CreateSubtasks.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Affected entity id (boardId for CreateSubtasks); null when the id is generated on apply.</summary>
    public Guid? EntityId { get; set; }

    /// <summary>
    /// jsonb — summary of the input the action is based on (token/cost conscious: excerpt only,
    /// never the raw prompt). Always valid JSON; initialised to <c>{}</c> so a row is insertable
    /// even before the service fills it in.
    /// </summary>
    public string Basis { get; set; } = "{}";

    /// <summary>jsonb — data before the action is applied (null for CreateSubtasks: nothing to revert).</summary>
    public string? BeforeSnapshot { get; set; }

    /// <summary>jsonb — the proposed change, applied only once the action is Approved.</summary>
    public string? AfterSnapshot { get; set; }

    /// <summary>jsonb — what was actually written on Approve (created task/label ids + warnings); the basis for Undo.</summary>
    public string? AppliedSnapshot { get; set; }

    public AiActionStatus Status { get; set; } = AiActionStatus.Pending;

    public Guid RequestedByUserId { get; set; }

    public Guid? DecidedByUserId { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }

    /// <summary>Optional reason recorded when the action is rejected or undone (≤ 500 chars).</summary>
    public string? DecisionNote { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public ApplicationUser? RequestedByUser { get; set; }

    public ApplicationUser? DecidedByUser { get; set; }
}
