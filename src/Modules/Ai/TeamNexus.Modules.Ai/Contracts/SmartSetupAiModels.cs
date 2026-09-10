namespace TeamNexus.Modules.Ai.Contracts;

/// <summary>
/// Raw Smart Setup output expected from the AI provider (Phase 3 §4.1). This mirrors the system
/// prompt exactly and is a deserialization target only — it never reaches the HTTP API and is
/// never persisted. <c>SmartSetupService</c> converts it into the normalized
/// <c>SmartSetupProposal</c> DTO.
/// </summary>
public sealed class AiSmartSetupOutput
{
    public string? Summary { get; set; }

    public List<AiSmartSetupTask> Tasks { get; set; } = [];
}

/// <summary>
/// One raw proposed sub-task. Every field is optional so a partially-conforming model answer
/// still deserializes; normalization (target lengths, priority vocabulary, label caps) happens
/// in <c>SmartSetupService</c> (Phase 3 §4.3 step 9).
/// </summary>
public sealed class AiSmartSetupTask
{
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>One of <c>Low|Medium|High|Urgent</c> or null/anything else → normalized to null.</summary>
    public string? Priority { get; set; }

    public List<string> Labels { get; set; } = [];

    /// <summary>Display name of a suggested workspace member (may not match anyone).</summary>
    public string? SuggestedAssignee { get; set; }
}
