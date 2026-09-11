using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// Counter snapshot taken <b>after</b> each loop turn (Phase 7 §4.8c). Pure data: the wall-clock
/// budget arrives as <see cref="Elapsed"/> from the caller so this type never reads a clock itself,
/// which is what makes the guardrail verifiable without AI or a database (D19).
/// </summary>
public sealed record AgentGuardrailState(
    int ToolCalls,
    int LlmCalls,
    int PromptTokens,
    int CompletionTokens,
    TimeSpan Elapsed)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}

/// <summary>
/// The four Phase 7 guardrails as one pure decision (D12/D19). Precedence is fixed and documented so
/// verification is deterministic: <b>ToolLimit → TokenBudget → TimeLimit</b>.
/// <para>
/// <c>MaxRunLlmCalls</c> deliberately has no stop reason of its own (the enum has no
/// <c>LlmLimit</c>): the orchestrator checks it <i>before</i> a provider call and reports
/// <see cref="AgentStopReason.InternalError"/>, because hitting it means the model kept producing
/// turns without ever finishing — a bug, not a budget the caller can raise.
/// </para>
/// </summary>
public static class AgentGuardrails
{
    /// <summary>Returns the stop reason once a threshold is exceeded, otherwise <c>null</c> (keep looping).</summary>
    public static AgentStopReason? Evaluate(AgentGuardrailState state, AgentEffectiveOptions options)
    {
        if (state.ToolCalls > options.MaxToolCalls)
        {
            return AgentStopReason.ToolLimit;
        }

        if (state.TotalTokens > options.MaxRunTokens)
        {
            return AgentStopReason.TokenBudget;
        }

        if (state.Elapsed > TimeSpan.FromSeconds(options.RunTimeoutSeconds))
        {
            return AgentStopReason.TimeLimit;
        }

        return null;
    }

    /// <summary>True when another provider round-trip is allowed (<c>MaxRunLlmCalls</c>).</summary>
    public static bool CanCallProvider(AgentGuardrailState state, AgentEffectiveOptions options)
        => state.LlmCalls < options.MaxRunLlmCalls;

    /// <summary>
    /// Human/Manager-facing explanation of a stop, naming the threshold that was exceeded <b>and</b>
    /// the numbers actually used — "báo Manager đúng việc cần làm" (D4/D12).
    /// </summary>
    public static string Describe(AgentStopReason reason, AgentGuardrailState state, AgentEffectiveOptions options)
        => reason switch
        {
            AgentStopReason.ToolLimit =>
                $"Vượt ngưỡng số lần gọi tool: đã gọi {state.ToolCalls} lần, giới hạn Agent:MaxToolCalls={options.MaxToolCalls}.",
            AgentStopReason.TokenBudget =>
                $"Vượt ngân sách token: đã dùng {state.TotalTokens} token (prompt {state.PromptTokens} + completion {state.CompletionTokens}), "
                + $"giới hạn Agent:MaxRunTokens={options.MaxRunTokens}.",
            AgentStopReason.TimeLimit =>
                $"Vượt thời gian chạy: đã chạy {Math.Round(state.Elapsed.TotalSeconds)}s, giới hạn Agent:RunTimeoutSeconds={options.RunTimeoutSeconds}s.",
            AgentStopReason.ProviderError =>
                "Nhà cung cấp AI trả về lỗi hoặc không phản hồi.",
            AgentStopReason.Cancelled =>
                "Lượt chạy đã bị con người huỷ.",
            AgentStopReason.TaskChanged =>
                "Task đã thay đổi trong lúc agent chạy (bị xoá, đổi người thực hiện hoặc rời cột 'Chờ làm rõ').",
            AgentStopReason.InternalError => "Lượt chạy dừng bất thường.",
            AgentStopReason.DraftProduced => "Agent đã soạn xong kết quả và đang chờ duyệt.",
            AgentStopReason.QuestionAsked => "Agent cần trưởng nhóm làm rõ thông tin.",
            _ => "Lượt chạy đã dừng.",
        };
}
