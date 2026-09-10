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
