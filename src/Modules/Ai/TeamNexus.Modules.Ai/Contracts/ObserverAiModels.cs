namespace TeamNexus.Modules.Ai.Contracts;

/// <summary>
/// Raw AI Observer output expected from the provider (Phase 5 §4.2). Mirrors the system prompt
/// exactly and is a deserialization target only: it never reaches the HTTP layer and is never
/// persisted as-is — <c>ObserverFindingValidator</c> converts it into the validated
/// <c>ObserverFinding</c> list first.
/// </summary>
public sealed class AiObserverOutput
{
    public List<AiObserverFinding> Findings { get; set; } = [];
}

/// <summary>
/// One raw AI finding. Every field is optional so a partially-conforming model answer still
/// deserializes; <b>nothing</b> here is trusted — type/severity vocabularies are checked and
/// evidence ids are intersected with the ids the detector actually found
/// (anti-hallucination, Phase 5 §4.2).
/// </summary>
public sealed class AiObserverFinding
{
    /// <summary>Should be one of <c>OverdueTask|StalledTask|Overload|Bottleneck</c>.</summary>
    public string? Type { get; set; }

    /// <summary>One of <c>Low|Medium|High|Critical</c> or null/unknown → normalized to Medium.</summary>
    public string? Severity { get; set; }

    public string? Title { get; set; }

    /// <summary>Human-readable explanation (Vietnamese, per the prompt), capped at 2000 chars.</summary>
    public string? Message { get; set; }

    /// <summary>Must be a subset of the matching signal's evidence; unknown ids are dropped.</summary>
    public List<Guid>? TaskIds { get; set; }

    /// <summary>Must be a subset of the matching signal's evidence; unknown ids are dropped.</summary>
    public List<Guid>? UserIds { get; set; }
}
