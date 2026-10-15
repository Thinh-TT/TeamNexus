using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Knowledge;

/// <summary>
/// Offline stand-in for <see cref="OpenRouterEmbeddingProvider"/> (Giai đoạn 14.7 Track B §B3), registered
/// whenever <c>Embeddings:Enabled</c> is on but no API key is configured.
/// <para>
/// <b>Deterministic by construction: the same text always produces the same vector.</b> The vector is derived
/// from a SHA-256 of the text, so the whole RAG path — chunking, storage, the cosine query, the context
/// budget — is verifiable offline with zero network and zero cost, exactly as <c>FakeAiProvider</c> makes the
/// Observer and the agent verifiable.
/// </para>
/// <para>
/// <b>Why not simply return zeros.</b> All-zero vectors are cosine-degenerate (every distance is undefined or
/// identical), so a nearest-neighbour assertion against them would pass for the wrong reason and would hide a
/// broken ordering. A hash-derived vector has real direction, which makes "the nearest chunk came back first"
/// a meaningful claim.
/// </para>
/// <para>
/// It is <b>not</b> semantically meaningful — it does not model similarity at all, so it must never be used to
/// judge retrieval quality. It exists so the plumbing can be tested, and a test asserts it is deterministic.
/// </para>
/// </summary>
public sealed class FakeEmbeddingProvider : IEmbeddingProvider
{
    private readonly EmbeddingOptions _options;
    private readonly ILogger<FakeEmbeddingProvider> _logger;

    public FakeEmbeddingProvider(
        IOptions<EmbeddingOptions> options,
        ILogger<FakeEmbeddingProvider> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task<IReadOnlyList<ReadOnlyMemory<float>>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);

        ct.ThrowIfCancellationRequested();

        var dimensions = _options.Dimensions <= 0 ? EmbeddingOptions.SchemaDimension : _options.Dimensions;

        var vectors = new List<ReadOnlyMemory<float>>(texts.Count);

        foreach (var text in texts)
        {
            vectors.Add(VectorFor(text, dimensions));
        }

        _logger.LogDebug(
            "FakeEmbeddingProvider: sinh {Count} vector tất định ({Dimensions} chiều) — không gọi mạng.",
            vectors.Count,
            dimensions);

        return Task.FromResult<IReadOnlyList<ReadOnlyMemory<float>>>(vectors);
    }

    /// <summary>
    /// Expands one SHA-256 digest into <paramref name="dimensions"/> components in <c>[-1, 1]</c>.
    /// <para>
    /// The digest has 32 bytes and the vector needs more components than that, so the hash is re-seeded with a
    /// block counter: bytes are produced deterministically until the vector is full. The result is normalised,
    /// which keeps cosine distances in a sane range instead of clustering near zero.
    /// </para>
    /// <para>
    /// An empty or whitespace-only text is <b>not</b> an error: it still yields a (stable) vector, because the
    /// alternative would be an exception in a streaming path whose caller cannot do anything about it.
    /// </para>
    /// </summary>
    private static float[] VectorFor(string? text, int dimensions)
    {
        var label = text ?? string.Empty;
        var bytes = Encoding.UTF8.GetBytes(label);

        var vector = new float[dimensions];
        var block = 0;
        var filled = 0;

        while (filled < dimensions)
        {
            var seed = new byte[bytes.Length + 4];
            Array.Copy(bytes, seed, bytes.Length);
            BitConverter.TryWriteBytes(seed.AsSpan(bytes.Length), block);

            var digest = SHA256.HashData(seed);

            for (var index = 0; index < digest.Length && filled < dimensions; index++, filled++)
            {
                // Map 0..255 onto -1..1 so the components have both signs.
                vector[filled] = (digest[index] / 127.5f) - 1f;
            }

            block++;
        }

        Normalize(vector);

        return vector;
    }

    /// <summary>Unit-length scaling; a no-op for the (impossible here) all-zero vector, which stays readable.</summary>
    private static void Normalize(float[] vector)
    {
        var sumOfSquares = 0d;

        foreach (var component in vector)
        {
            sumOfSquares += component * component;
        }

        var length = Math.Sqrt(sumOfSquares);

        if (length <= double.Epsilon)
        {
            return;
        }

        for (var index = 0; index < vector.Length; index++)
        {
            vector[index] = (float)(vector[index] / length);
        }
    }
}
