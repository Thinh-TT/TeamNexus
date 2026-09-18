namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// Body of <c>POST /api/workspaces/{workspaceId}/smart-setup/template</c> (Phase 14 §4, decision D12):
/// the team lead's free-text description of the project the AI should structure into a board.
/// Same bound as the Phase 3 sub-task proposal (<c>SmartSetupService.MaxDescriptionLength</c>).
/// </summary>
public sealed record BoardTemplateRequest(string? Description);

/// <summary>
/// One proposed column. <see cref="IsDone"/> marks the lane whose tasks count as finished; the server
/// pins <b>exactly one</b> such column (decision D13), because <c>ReportAggregator</c> and the
/// dashboard derive every completion number from it.
/// </summary>
public sealed record BoardTemplateColumnProposal(string Name, bool IsDone);

/// <summary>
/// One proposed starter task. <see cref="ColumnName"/> is a <b>name</b>, not an id: the columns do not
/// exist yet. The applier resolves it against the confirmed column list, and an unknown name lands in
/// the first non-done column rather than dropping the task.
/// </summary>
public sealed record BoardTemplateTaskProposal(
    string Title,
    string? Description,
    string? Priority,
    string ColumnName,
    IReadOnlyList<SmartSetupLabelSuggestion> Labels,
    SmartSetupAssigneeSuggestion? Assignee);

/// <summary>
/// A whole board proposal: one board + its columns + its starter tasks. Phase 14 never persists this
/// directly — the Accountability Layer does, and only after human approval.
/// <para>
/// The same shape is echoed back by "generate" and accepted by "confirm", so the frontend edits exactly
/// the object it received and never has to translate between two contracts.
/// </para>
/// </summary>
public sealed record BoardTemplateProposal(
    string? Summary,
    string BoardName,
    string? BoardDescription,
    IReadOnlyList<BoardTemplateColumnProposal> Columns,
    IReadOnlyList<BoardTemplateTaskProposal> Tasks);

/// <summary>
/// Body of <c>POST .../smart-setup/template/confirm</c> — the proposal after the team lead reviewed it.
/// </summary>
public sealed record ConfirmBoardTemplateRequest(BoardTemplateProposal? Proposal);
