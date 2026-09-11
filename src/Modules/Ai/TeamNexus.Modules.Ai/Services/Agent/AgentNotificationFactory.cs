using System.Text.Json;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// Builds the persisted shape of an agent alert (Phase 7 §4.8d). Pure and public for the same reason
/// as <see cref="ObserverNotificationFactory"/>: the payload shape and the message wording can be
/// verified (and tuned) without a database, and the Manager-facing text always names the threshold
/// that was exceeded plus the numbers actually used.
/// </summary>
public static class AgentNotificationFactory
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>jsonb <c>payload</c>: trace the alert back to the run and show the guardrail numbers.</summary>
    public static string BuildPayload(AgentRun run, AgentEffectiveOptions options)
        => JsonSerializer.Serialize(new
        {
            runId = run.Id,
            taskId = run.TaskId,
            boardId = run.BoardId,
            workspaceId = run.WorkspaceId,
            status = run.Status.ToString(),
            stopReason = run.StopReason?.ToString(),
            toolCallCount = run.ToolCallCount,
            llmCallCount = run.LlmCallCount,
            promptTokens = run.PromptTokens,
            completionTokens = run.CompletionTokens,
            totalTokens = run.PromptTokens + run.CompletionTokens,
            thresholds = new
            {
                maxToolCalls = options.MaxToolCalls,
                runTimeoutSeconds = options.RunTimeoutSeconds,
                maxRunTokens = options.MaxRunTokens,
                maxRunLlmCalls = options.MaxRunLlmCalls,
            },
            aiActionLogId = run.AiActionLogId,
            clarificationCommentId = run.ClarificationCommentId,
        }, Json);

    /// <summary>Notification title per type (Vietnamese, short enough for the 200-char column).</summary>
    public static string BuildTitle(string type) => type switch
    {
        AgentNotificationTypes.RunFailed => "AI Agent dừng bất thường",
        AgentNotificationTypes.AwaitingClarification => "AI Agent cần bạn làm rõ",
        AgentNotificationTypes.OutputPending => "AI Agent có kết quả chờ duyệt",
        _ => "AI Agent",
    };

    /// <summary>
    /// Notification body per type. For a failure it states which threshold was exceeded and how much
    /// was used, so the team lead knows whether to raise a limit or re-run (D4/D12).
    /// </summary>
    public static string BuildMessage(string type, AgentRun run, AgentEffectiveOptions options)
    {
        var task = $"Task {run.TaskId}";
        var reason = run.StopReason;

        return type switch
        {
            AgentNotificationTypes.RunFailed => reason is { } stop
                ? Truncate(
                    $"{task}: {AgentGuardrails.Describe(stop, State(run), options)}"
                    + (string.IsNullOrWhiteSpace(run.Error) ? string.Empty : $" Chi tiết: {run.Error}"),
                    2000)
                : Truncate($"{task}: lượt chạy đã dừng bất thường.", 2000),

            AgentNotificationTypes.AwaitingClarification => Truncate(
                $"{task}: AI Agent cần trưởng nhóm trả lời: {run.ClarificationQuestion ?? "(không có nội dung)"}",
                2000),

            AgentNotificationTypes.OutputPending => Truncate(
                $"{task}: AI Agent đã soạn xong kết quả ({run.OutputKind ?? "không xác định"}) và đang chờ duyệt.",
                2000),

            _ => Truncate($"{task}: cập nhật từ AI Agent.", 2000),
        };
    }

    private static AgentGuardrailState State(AgentRun run)
        => new(
            run.ToolCallCount,
            run.LlmCallCount,
            run.PromptTokens,
            run.CompletionTokens,
            Elapsed: run.FinishedAt is { } finished ? finished - run.StartedAt : TimeSpan.Zero);

    private static string Truncate(string value, int limit)
        => value.Length <= limit ? value : value[..limit];
}
