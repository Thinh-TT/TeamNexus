using TeamNexus.Modules.Ai.Services.Knowledge;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// An <see cref="IEmbeddingProvider"/> that answers with a deliberately wrong vector length
/// (Giai đoạn 14.7 Track B §B3).
/// <para>
/// <b>Why this is worth a double.</b> The schema pins the column at <c>vector(1536)</c>, so a provider that
/// returned a different length would make <i>every</i> insert fail with a database message that names no
/// document — and the embedding pass would look like a mysterious stall rather than a configuration mistake.
/// The runner is supposed to notice the mismatch itself and record it on the document, which is exactly what
/// this double lets a suite observe.
/// </para>
/// <para>
/// It never throws: the "provider is down" path has its own double
/// (<see cref="ThrowingEmbeddingProvider"/>), and conflating the two would leave one of the two branches
/// untested.
/// </para>
/// </summary>
public sealed class WrongDimensionEmbeddingProvider : IEmbeddingProvider
{
    /// <summary>A plausible-looking model dimension that is not the schema's.</summary>
    public const int Dimension = 768;

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var vectors = texts
            .Select(_ => new ReadOnlyMemory<float>(Enumerable.Repeat(0.5f, Dimension).ToArray()))
            .ToList();

        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(vectors);
    }
}

/// <summary>
/// An <see cref="IEmbeddingProvider"/> that fails every call (Giai đoạn 14.7 Track B §B3).
/// <para>
/// Models the case the runner exists for: a third-party embedder is unreachable. The contract under test is
/// that the document ends up <c>failed</c> with a reason rather than sitting in <c>pending</c> forever, and
/// that a human's approve request was never coupled to this call.
/// </para>
/// </summary>
public sealed class ThrowingEmbeddingProvider : IEmbeddingProvider
{
    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
        => throw new EmbeddingProviderException("Nhà cung cấp embedding không phản hồi (double kiểm thử).");
}
