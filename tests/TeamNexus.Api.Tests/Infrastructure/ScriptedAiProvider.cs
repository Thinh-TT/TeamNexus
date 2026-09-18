using System.Runtime.CompilerServices;
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
/// <para>
/// <b>Phase 14 (§2.1, P7):</b> it also implements <see cref="IAiStreamingProvider"/>, so the chat's SSE
/// pipeline can be driven chunk by chunk — including the failure path, which the offline
/// <c>FakeAiProvider</c> deliberately never produces. The three ports share one instance because the
/// production registration does the same thing (one stateless transport over one switch), and because a
/// suite that scripts an answer usually wants that answer regardless of which port the service picks.
/// </para>
/// </summary>
public sealed class ScriptedAiProvider : IAiProvider, IAiToolCallingProvider, IAiStreamingProvider
{
    /// <summary>Always answers with this content; set to something unparsable for the error path.</summary>
    public string Content { get; init; } = "{}";

    /// <summary>How many times the provider was asked (the service retries once on bad JSON).</summary>
    public int CallCount { get; private set; }

    /// <summary>Prompt of the last call, so a suite can assert what the service asked for.</summary>
    public AiCompletionRequest? LastRequest { get; private set; }

    // ---- streaming script (Phase 14) ---------------------------------------

    /// <summary>
    /// Chunks the streaming answer is delivered in, in order. Defaults to a single short answer so a
    /// suite that only cares about the non-streaming paths needs no setup at all.
    /// </summary>
    public IReadOnlyList<string> StreamChunks { get; init; } = ["Xin chào, ", "đây là ", "câu trả lời mẫu."];

    /// <summary>When true, <see cref="StreamAsync"/> throws mid-stream instead of answering.</summary>
    public bool ThrowOnStream { get; init; }

    /// <summary>Message carried by the thrown <see cref="AiProviderException"/> (asserted verbatim).</summary>
    public string StreamErrorMessage { get; init; } = "DeepSeek không phản hồi trong 60s (timeout).";

    /// <summary>
    /// Throw after this many chunks have been yielded, so the "error arrives mid-stream" case can be
    /// tested with text the client already received. 0 means "throw before any text".
    /// </summary>
    public int ThrowAfterChunks { get; init; }

    /// <summary>How many times the streaming port was called (0 proves a guard short-circuited).</summary>
    public int StreamCallCount { get; private set; }

    /// <summary>Request of the last streamed call, so a suite can assert the guardrails that were applied.</summary>
    public AiStreamRequest? LastStreamRequest { get; private set; }

    public Task<AiCompletionResult> CompleteAsync(AiCompletionRequest request, CancellationToken ct = default)
    {
        CallCount++;
        LastRequest = request;

        return Task.FromResult(new AiCompletionResult(Content, PromptTokens: 10, CompletionTokens: 20));
    }

    /// <summary>
    /// Function-calling turn: never used by the chat suites, so it fails loudly rather than silently
    /// answering something a test might mistake for the chat stream.
    /// </summary>
    public Task<AiChatResult> ChatAsync(AiChatRequest request, CancellationToken ct = default)
        => throw new AiProviderException("ScriptedAiProvider: IAiToolCallingProvider không được script trong suite này.");

    public async IAsyncEnumerable<AiStreamChunk> StreamAsync(
        AiStreamRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        StreamCallCount++;
        LastStreamRequest = request;

        if (ThrowOnStream && ThrowAfterChunks == 0)
        {
            throw new AiProviderException(StreamErrorMessage);
        }

        var promptTokens = 11;
        var completionTokens = 0;

        for (var i = 0; i < StreamChunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (ThrowOnStream && i >= ThrowAfterChunks)
            {
                throw new AiProviderException(StreamErrorMessage);
            }

            completionTokens += StreamChunks[i].Length / 4;

            // Deterministic interleaving without a timer: the pipeline must cope with one chunk per
            // turn of the enumerator, which is what Task.Yield guarantees.
            await Task.Yield();

            var isLast = i == StreamChunks.Count - 1;

            yield return new AiStreamChunk(
                StreamChunks[i],
                isLast ? promptTokens : null,
                isLast ? completionTokens : null,
                Done: isLast);
        }

        if (StreamChunks.Count == 0)
        {
            yield return new AiStreamChunk(null, promptTokens, completionTokens, Done: true);
        }
    }
}
