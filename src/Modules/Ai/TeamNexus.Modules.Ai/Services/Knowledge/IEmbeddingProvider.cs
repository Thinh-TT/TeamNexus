namespace TeamNexus.Modules.Ai.Services.Knowledge;

/// <summary>
/// The embedding port behind the semantic knowledge base (Giai đoạn 14.7 Track B §B3).
/// <para>
/// <b>A separate port, not a method on <see cref="IJevDecisionProvider"/> or <c>IAiProvider</c>.</b> All three
/// speak different shapes — typed decisions, chat completions, and now an array of floats — and folding them
/// together would force every implementation and every test double to change whenever one of them did. This
/// is the same reasoning that produced <c>IAiToolCallingProvider</c> (Phase 7) and <c>IAiStreamingProvider</c>
/// (Phase 14).
/// </para>
/// <para>
/// <b>Fixed length in, fixed length out.</b> The returned vectors must have
/// <c>Embeddings:Dimensions</c> elements, which the schema pins at 1536. Providers that report a different
/// length are refused rather than accepted, because storing them would make every later cosine comparison
/// fail in the database with no clue as to which runs produced the bad rows.
/// </para>
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Embeds one or more texts, preserving order. An empty input returns an empty list without a network call.
    /// </summary>
    Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);
}

/// <summary>
/// Raised for every transport/parse failure of an embedding call (the <c>AiProviderException</c> precedent).
/// <para>
/// Callers are expected to <b>absorb</b> it: the embedding runner records <c>status = 'failed'</c> on the
/// document and moves on, because a knowledge document that could not be embedded must never break a request
/// a human is waiting for.
/// </para>
/// </summary>
public sealed class EmbeddingProviderException : Exception
{
    public EmbeddingProviderException(string message)
        : base(message)
    {
    }

    public EmbeddingProviderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
