using System.Text.Json;

namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// Body for the (Phase 4 §3) confirm endpoint and the shape stored in
/// <c>ai_action_logs.after_snapshot</c>: the proposal as edited/confirmed by the user on the UI.
/// </summary>
/// <param name="Description">The team lead's original description — kept as the basis of the action.</param>
/// <param name="Summary">Optional AI summary (capped when stored).</param>
/// <param name="Tasks">The sub-tasks to create when the action is approved (1..MaxTaskCount).</param>
/// <param name="ColumnId">Target column; null ⇒ first column of the board (by position).</param>
public sealed record ConfirmSmartSetupRequest(
    string Description,
    string? Summary,
    IReadOnlyList<SmartSetupTaskProposal> Tasks,
    Guid? ColumnId);

/// <summary>Body for rejecting an AI action (Phase 4 §3).</summary>
public sealed record RejectAiActionRequest(string? Note);

/// <summary>Compact AI-action row for the board-scoped history list (Phase 4 §3.1).</summary>
public sealed record AiActionLogResponse(
    Guid Id,
    string Action,
    string EntityType,
    Guid? EntityId,
    string Status,
    Guid RequestedByUserId,
    string? RequestedByName,
    Guid? DecidedByUserId,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    int TaskCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Full AI-action detail: the raw jsonb snapshots (parsed defensively — null when a value is
/// absent or not valid JSON) plus the ids written when it was approved.
/// </summary>
public sealed record AiActionLogDetailResponse(
    Guid Id,
    string Action,
    string EntityType,
    Guid? EntityId,
    string Status,
    Guid RequestedByUserId,
    string? RequestedByName,
    Guid? DecidedByUserId,
    string? DecidedByName,
    DateTimeOffset? DecidedAt,
    string? DecisionNote,
    JsonElement? Basis,
    JsonElement? BeforeSnapshot,
    JsonElement? AfterSnapshot,
    JsonElement? AppliedSnapshot,
    IReadOnlyList<Guid> CreatedTaskIds,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
