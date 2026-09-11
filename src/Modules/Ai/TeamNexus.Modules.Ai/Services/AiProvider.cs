namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Provider-agnostic port for AI text completion (Phase 3 §2.1). Deliberately thin so the
/// Smart Setup flow (Phase 3 §4) and the AI Observer (Phase 5) reuse it by changing only
/// the prompt — swapping the implementation (DeepSeek ↔ Fake ↔ future provider) must not
/// require business-logic changes.
/// </summary>
public interface IAiProvider
{
    /// <summary>
    /// Runs one completion. Implementations map every transport/parse failure to
    /// <see cref="AiProviderException"/> ( → 502) and never leak the API key into messages or logs.
    /// </summary>
    Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default);
}

/// <summary>
/// One completion call. <paramref name="JsonMode"/> asks the provider to constrain the answer
/// to a single JSON object (DeepSeek's <c>response_format = { "type": "json_object" }</c>).
/// </summary>
public sealed record AiCompletionRequest(
    string SystemPrompt,
    string UserPrompt,
    double Temperature,
    int MaxTokens,
    bool JsonMode);

/// <summary>
/// Completion output. Token counts come from the provider's <c>usage</c> block and stay
/// nullable: not every provider (e.g. <c>FakeAiProvider</c>) reports them.
/// </summary>
public sealed record AiCompletionResult(
    string Content,
    int? PromptTokens,
    int? CompletionTokens);

/// <summary>
/// Provider capable of OpenAI-compatible <b>function calling</b> (Phase 7 §4.2, decision S6).
/// <para>
/// Deliberately a SECOND port instead of an extra member on <see cref="IAiProvider"/>: the
/// Smart Setup / Observer flows (Phase 3/5) must keep compiling and behaving byte-identically, and
/// the two shapes genuinely differ — between turns the caller has to send back both the assistant
/// message that carried <c>tool_calls</c> and one <c>tool</c> message per
/// <c>tool_call_id</c>.
/// </para>
/// </summary>
public interface IAiToolCallingProvider
{
    /// <summary>
    /// Runs one multi-turn chat round. Transport failures (HTTP ≠ 2xx, timeout, unparsable JSON)
    /// map to <see cref="AiProviderException"/> (502) exactly like <see cref="IAiProvider"/>; the
    /// API key never reaches a log or an exception message and there is no retry.
    /// <para>
    /// Unlike <see cref="IAiProvider"/>, an empty <c>content</c> is NOT an error: a turn that only
    /// emitted tool calls legitimately has none.
    /// </para>
    /// </summary>
    Task<AiChatResult> ChatAsync(AiChatRequest request, CancellationToken ct = default);
}

/// <summary>One tool advertised to the model. <paramref name="ParametersJsonSchema"/> is raw JSON Schema.</summary>
public sealed record AiToolDefinition(string Name, string Description, string ParametersJsonSchema);

/// <summary>One tool call as emitted by the model (or echoed back in the assistant message).</summary>
public sealed record AiToolInvocation(string Id, string Name, string ArgumentsJson);

/// <summary>
/// One chat message. <c>role</c> ∈ {system, user, assistant, tool}.
/// <list type="bullet">
///   <item><c>tool</c> ⇒ <see cref="ToolCallId"/> is required (which call it answers).</item>
///   <item><c>assistant</c> ⇒ may carry <see cref="ToolCalls"/> (and then usually a null content).</item>
/// </list>
/// </summary>
public sealed record AiChatMessage(
    string Role,
    string? Content,
    string? ToolCallId = null,
    string? Name = null,
    IReadOnlyList<AiToolInvocation>? ToolCalls = null);

/// <summary>One chat round request: system prompt, full message history, the tool whitelist and sampling knobs.</summary>
public sealed record AiChatRequest(
    string SystemPrompt,
    IReadOnlyList<AiChatMessage> Messages,
    IReadOnlyList<AiToolDefinition> Tools,
    double Temperature,
    int MaxTokens);

/// <summary>
/// One chat round result. <paramref name="FinishReason"/> is the provider's raw value
/// (<c>"stop"</c> | <c>"tool_calls"</c> | <c>"length"</c>); <c>"length"</c> is NOT an error — the
/// orchestrator maps it to <c>AgentStopReason.TokenBudget</c> (Phase 7 §4.8).
/// </summary>
public sealed record AiChatResult(
    string? Content,
    IReadOnlyList<AiToolInvocation> ToolCalls,
    int? PromptTokens,
    int? CompletionTokens,
    string FinishReason);
