using System.Text.Json;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// One agent run for the Kanban/agent panel (Phase 7 §4.10). Shape is frozen by the §5.1a contract —
/// any change has to be made on both sides.
/// <para>
/// The produced content and any base64 payload are deliberately <b>absent</b>: reading the result is
/// the Accountability drawer's job (<c>GET /api/ai-actions/{logId}</c>), and duplicating a 512 KB body
/// into every list response would be wasteful.
/// </para>
/// </summary>
public sealed record AgentRunResponse(
    Guid Id,
    Guid TaskId,
    Guid BoardId,
    Guid AgentUserId,
    string AgentDisplayName,
    Guid TriggeredByUserId,
    string TriggeredByName,
    string Status,
    string? StopReason,
    string? ClarificationQuestion,
    Guid? AiActionLogId,
    string? OutputKind,
    string? Error,
    int ToolCallCount,
    int LlmCallCount,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    bool TraceTruncated)
{
    /// <summary>One-way mapping (precedent: <c>ObserverRunFinding.From</c>).</summary>
    public static AgentRunResponse From(AgentRun run, string? agentDisplayName, string? triggeredByName)
        => new(
            run.Id,
            run.TaskId,
            run.BoardId,
            run.AgentUserId,
            agentDisplayName ?? string.Empty,
            run.TriggeredByUserId,
            triggeredByName ?? string.Empty,
            run.Status.ToString(),
            run.StopReason?.ToString(),
            run.ClarificationQuestion,
            run.AiActionLogId,
            run.OutputKind,
            run.Error,
            run.ToolCallCount,
            run.LlmCallCount,
            run.PromptTokens,
            run.CompletionTokens,
            run.PromptTokens + run.CompletionTokens,
            run.StartedAt,
            run.FinishedAt,
            run.TraceTruncated);
}

/// <summary>One entry of <c>agent_runs.tool_call_trace</c>.</summary>
public sealed record AgentToolTraceEntryResponse(
    string Name,
    string? Arguments,
    string? ResultSummary,
    bool IsError,
    DateTimeOffset At,
    int DurationMs);

/// <summary>Run detail: the row plus its tool trace and the clarification conversation context.</summary>
public sealed record AgentRunDetailResponse(
    AgentRunResponse Run,
    IReadOnlyList<AgentToolTraceEntryResponse> ToolCallTrace,
    Guid? PreviousRunId,
    string? PreviousQuestion,
    string? ResolutionCommentContent)
{
    public static AgentRunDetailResponse From(
        AgentRun run,
        string? agentDisplayName,
        string? triggeredByName,
        IReadOnlyList<AgentToolTraceEntryResponse> toolCallTrace,
        string? previousQuestion,
        string? resolutionCommentContent)
        => new(
            AgentRunResponse.From(run, agentDisplayName, triggeredByName),
            toolCallTrace,
            run.PreviousRunId,
            previousQuestion,
            resolutionCommentContent);
}

/// <summary>One agent-produced file (Phase 7 §4.10). Never carries the bytes — those come from the download route.</summary>
public sealed record AttachmentResponse(
    Guid Id,
    Guid TaskId,
    string FileName,
    string ContentType,
    int SizeBytes,
    Guid CreatedByUserId,
    string CreatedByName,
    Guid? SourceRunId,
    DateTimeOffset CreatedAt)
{
    public static AttachmentResponse From(TaskAttachment attachment, string? createdByName)
        => new(
            attachment.Id,
            attachment.TaskId,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.CreatedByUserId,
            createdByName ?? string.Empty,
            attachment.SourceRunId,
            attachment.CreatedAt);
}

/// <summary>
/// Defensive parser for the jsonb tool trace. A malformed entry is skipped rather than failing the
/// read: the trace is diagnostic data, and the whole point of returning it is that a Manager can
/// inspect a failed run.
/// <para>
/// The trace is already capped when it is WRITTEN (D19), so nothing is re-truncated here.
/// </para>
/// </summary>
public static class AgentToolTraceParser
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static IReadOnlyList<AgentToolTraceEntryResponse> Parse(string? traceJson)
    {
        if (string.IsNullOrWhiteSpace(traceJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(traceJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            var entries = new List<AgentToolTraceEntryResponse>();
            foreach (var element in document.RootElement.EnumerateArray())
            {
                try
                {
                    var entry = element.Deserialize<AgentToolTraceEntryResponse>(Json);
                    if (entry is not null)
                    {
                        entries.Add(entry);
                    }
                }
                catch (JsonException)
                {
                    // Skip the damaged entry, keep the rest.
                }
            }

            return entries;
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
