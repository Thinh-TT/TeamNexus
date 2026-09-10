namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// Request body for POST /api/boards/{boardId}/smart-setup (Phase 3 §4.2): the team lead's
/// free-text description of the feature/project the AI should break down.
/// </summary>
public sealed record SmartSetupRequest(string Description);

/// <summary>Resolved assignee suggestion — <see cref="Matched"/> is false when the AI named someone not in the workspace.</summary>
public sealed record SmartSetupAssigneeSuggestion(Guid? UserId, string? DisplayName, bool Matched);

/// <summary>Resolved label suggestion — <see cref="Exists"/> true when the workspace already has that label.</summary>
public sealed record SmartSetupLabelSuggestion(Guid? LabelId, string Name, bool Exists);

/// <summary>One proposed sub-task. <see cref="Priority"/> null when the AI omitted/invalidated it; <see cref="Assignee"/> null when none was suggested.</summary>
public sealed record SmartSetupTaskProposal(
    string Title,
    string? Description,
    string? Priority,
    IReadOnlyList<SmartSetupLabelSuggestion> Labels,
    SmartSetupAssigneeSuggestion? Assignee);

/// <summary>
/// The full proposal returned to the UI for human editing + confirmation (Phase 3 §4.2).
/// Phase 3 never persists this — Phase 4 hands it to the Accountability Layer.
/// </summary>
public sealed record SmartSetupProposal(
    string? Summary,
    IReadOnlyList<SmartSetupTaskProposal> Tasks);
