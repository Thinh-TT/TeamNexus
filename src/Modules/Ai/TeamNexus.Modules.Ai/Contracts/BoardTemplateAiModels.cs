namespace TeamNexus.Modules.Ai.Contracts;

/// <summary>
/// Raw AI output for the board template (Phase 14 §4). Mirrors the system prompt exactly and is a
/// deserialization target only: it never reaches the HTTP layer and is never persisted as-is —
/// <c>BoardTemplateValidator</c> converts it into the validated <c>BoardTemplateProposal</c> first.
/// </summary>
public sealed class AiBoardTemplateOutput
{
    public string? Summary { get; set; }

    public string? BoardName { get; set; }

    public string? BoardDescription { get; set; }

    public List<AiBoardTemplateColumn>? Columns { get; set; }

    public List<AiBoardTemplateTask>? Tasks { get; set; }
}

/// <summary>
/// One raw column. Every field is optional and nothing here is trusted — a missing name drops the
/// column, an absent <c>isDone</c> is the validator's job to resolve.
/// </summary>
public sealed class AiBoardTemplateColumn
{
    public string? Name { get; set; }

    /// <summary>Should be <c>true</c> for the lane whose tasks count as finished.</summary>
    public bool? IsDone { get; set; }
}

/// <summary>
/// One raw task. Same "nothing is trusted" posture as the Phase 3 sub-task proposal: the title is
/// mandatory and bounded, everything else is resolved or dropped by the validator.
/// </summary>
public sealed class AiBoardTemplateTask
{
    public string? Title { get; set; }

    public string? Description { get; set; }

    /// <summary>Should be one of <c>Low|Medium|High|Urgent</c>; anything else becomes null.</summary>
    public string? Priority { get; set; }

    /// <summary>Name of the proposed column this task belongs to (ids do not exist yet).</summary>
    public string? ColumnName { get; set; }

    public List<string>? Labels { get; set; }

    public string? SuggestedAssignee { get; set; }
}
