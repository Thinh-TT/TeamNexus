using TeamNexus.Modules.Ai.Services;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// An <see cref="IAiProvider"/> that answers with content the test chooses.
/// <para>
/// The production <c>FakeAiProvider</c> only ever returns schema-valid Smart Setup JSON, which is
/// exactly what makes the offline suites cheap — but it also means the <b>malformed output</b> path
/// (two retries, then 400, and no row written) would otherwise be untestable without calling a real
/// model. This stub closes that gap deterministically.
/// </para>
/// </summary>
public sealed class ScriptedAiProvider : IAiProvider
{
    /// <summary>Always answers with this content; set to something unparsable for the error path.</summary>
    public string Content { get; init; } = "{}";

    /// <summary>How many times the provider was asked (the service retries once on bad JSON).</summary>
    public int CallCount { get; private set; }

    /// <summary>Prompt of the last call, so a suite can assert what the service asked for.</summary>
    public AiCompletionRequest? LastRequest { get; private set; }

    public Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default)
    {
        CallCount++;
        LastRequest = request;

        return Task.FromResult(new AiCompletionResult(Content, PromptTokens: 10, CompletionTokens: 20));
    }
}
