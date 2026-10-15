namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// What a taxonomy entry describes (Giai đoạn 14.7 §A2). Stored as lowercase text with a CHECK, so the
/// two families can never be mixed up by a query that forgets to filter.
/// </summary>
public enum TaxonomyKind
{
    /// <summary>Technical domain: Backend, Frontend, DevOps, …</summary>
    Domain,

    /// <summary>Kind of work: Feature, Bug, Refactor, …</summary>
    WorkType,
}

/// <summary>
/// One fixed, workspace-scoped vocabulary entry that Jev answers <c>Choice</c> questions against
/// (Giai đoạn 14.7 §A2, Trụ cột 1).
/// <para>
/// <b>Why a table instead of free-text labels.</b> A <c>Choice</c> question is only as good as its option
/// list: if the labels live as free text on each board (<c>fe</c>, <c>frontend</c>, <c>ui-bug</c>), the same
/// domain fragments into several options, the probability mass splits across them, and no automatic mapping
/// is possible. A fixed per-workspace dictionary is what makes the answer machine-readable.
/// </para>
/// <para>
/// <b><see cref="Key"/> vs <see cref="Label"/>.</b> The key is the stable machine value the API and the
/// database agree on; the label is what a human reads and what is shown to the model as the option
/// description. Renaming a label is therefore a display change, not a data migration.
/// </para>
/// <para>
/// Entries are <b>seeded lazily and idempotently</b> on first read by <c>TaxonomyService</c>, so an existing
/// workspace gains the standard vocabulary without a data migration and without a background job.
/// </para>
/// </summary>
public class WorkspaceTaxonomy : IAuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WorkspaceId { get; set; }

    public TaxonomyKind Kind { get; set; }

    /// <summary>Stable machine value (<c>Backend</c>, <c>TechDebt</c>, …) — unique per workspace and kind.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-facing name, also used as the option description handed to Jev.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Optional <c>#RRGGBB</c> badge colour for the UI; null means "use the default".</summary>
    public string? ColorHex { get; set; }

    /// <summary>Display order within a kind (ascending). Ties fall back to <see cref="Key"/>.</summary>
    public int Position { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public Workspace? Workspace { get; set; }
}
