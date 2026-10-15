using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services.Knowledge;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Giai đoạn 14.7 Track B §B3 — <see cref="FakeEmbeddingProvider"/>, purely.
/// <para>
/// The offline embedder is what makes the whole RAG path verifiable with zero network and zero cost, so its
/// two load-bearing properties are asserted here: <b>determinism</b> (the same text always produces the same
/// vector, which is what lets a retrieval assertion be exact) and <b>a real direction</b> (never all-zero,
/// because cosine distance between zero vectors is meaningless and a nearest-neighbour test would then pass
/// for the wrong reason).
/// </para>
/// </summary>
public sealed class FakeEmbeddingProviderTests
{
    private static FakeEmbeddingProvider Provider(int dimensions = EmbeddingOptions.SchemaDimension)
        => new(
            Options.Create(new EmbeddingOptions { Dimensions = dimensions }),
            NullLogger<FakeEmbeddingProvider>.Instance);

    [Fact]
    public async Task EmbedAsync_WithTheSameTextTwice_ReturnsTheSameVector()
    {
        // ⛔ Determinism is not a nicety: the retrieval suite compares an exact query vector, so a provider
        // that answered differently on the second call would make every cosine assertion flaky.
        var provider = Provider();

        var first = await provider.EmbedAsync(["Quy ước đặt tên nhánh"]);
        var second = await provider.EmbedAsync(["Quy ước đặt tên nhánh"]);

        Assert.Equal(first[0].ToArray(), second[0].ToArray());
    }

    [Fact]
    public async Task EmbedAsync_ProducesAVectorOfTheConfiguredDimension()
    {
        var provider = Provider(EmbeddingOptions.SchemaDimension);

        var vectors = await provider.EmbedAsync(["một đoạn văn"]);

        Assert.Equal(EmbeddingOptions.SchemaDimension, vectors[0].Length);
    }

    [Fact]
    public async Task EmbedAsync_HonoursANonDefaultDimension()
    {
        // A wrong-length vector must never reach a vector(1536) column, and the provider is the first place
        // the length can be observed.
        var provider = Provider(768);

        var vectors = await provider.EmbedAsync(["một đoạn văn"]);

        Assert.Equal(768, vectors[0].Length);
    }

    [Fact]
    public async Task EmbedAsync_IsNonZeroAndUnitLengthSoCosineDistanceIsMeaningful()
    {
        var provider = Provider();

        var vector = (await provider.EmbedAsync(["một đoạn văn"]))[0].ToArray();

        Assert.Contains(vector, component => component != 0f);

        var norm = Math.Sqrt(vector.Sum(component => (double)component * component));
        Assert.Equal(1d, norm, 4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public async Task EmbedAsync_WithBlankText_DoesNotThrowAndStillProducesAUsableVector(string? text)
    {
        // The alternative would be an exception in a path whose caller cannot do anything about it — an empty
        // chunk is a data problem, not a transport one.
        var provider = Provider();

        var vectors = await provider.EmbedAsync([text!]);

        Assert.Single(vectors);
        Assert.Equal(EmbeddingOptions.SchemaDimension, vectors[0].Length);
        Assert.Contains(vectors[0].ToArray(), component => component != 0f);
    }

    [Fact]
    public async Task EmbedAsync_PreservesInputOrder()
    {
        var provider = Provider();

        var texts = new[] { "alpha", "beta", "gamma" };

        var vectors = await provider.EmbedAsync(texts);

        Assert.Equal(3, vectors.Count);

        // Order is the contract the runner relies on: it zips `texts[i]` with `vectors[i]` when writing.
        for (var index = 0; index < texts.Length; index++)
        {
            var alone = await provider.EmbedAsync([texts[index]]);
            Assert.Equal(alone[0].ToArray(), vectors[index].ToArray());
        }
    }

    [Fact]
    public async Task EmbedAsync_DifferentTextProducesADifferentVector()
    {
        // Without this the retrieval suite would pass even if every chunk were embedded identically.
        var provider = Provider();

        var vectors = await provider.EmbedAsync(["quy ước đặt tên", "quy trình phát hành"]);

        Assert.NotEqual(vectors[0].ToArray(), vectors[1].ToArray());
    }

    [Fact]
    public async Task EmbedAsync_WithAnEmptyInputList_ReturnsAnEmptyListWithoutCallingAnything()
    {
        var provider = Provider();

        var vectors = await provider.EmbedAsync([]);

        Assert.Empty(vectors);
    }

    [Fact]
    public async Task EmbedAsync_WithManyTexts_ReturnsOneVectorPerText()
    {
        var provider = Provider();

        var texts = Enumerable.Range(0, 40).Select(i => $"đoạn số {i}").ToList();

        var vectors = await provider.EmbedAsync(texts);

        Assert.Equal(texts.Count, vectors.Count);
        Assert.All(vectors, vector => Assert.Equal(EmbeddingOptions.SchemaDimension, vector.Length));
    }

    [Fact]
    public async Task EmbedAsync_WithACancelledToken_ThrowsInsteadOfDoingWork()
    {
        var provider = Provider();

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.EmbedAsync(["một đoạn văn"], cancellation.Token));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task EmbedAsync_WithANonPositiveDimension_FallsBackToTheSchemaDimension(int dimensions)
    {
        // A misconfigured 0 would otherwise produce a zero-length vector, which Pgvector refuses and which no
        // caller can interpret.
        var provider = Provider(dimensions);

        var vectors = await provider.EmbedAsync(["một đoạn văn"]);

        Assert.Equal(EmbeddingOptions.SchemaDimension, vectors[0].Length);
    }

    [Fact]
    public async Task EmbedAsync_IsIndependentOfCallOrder()
    {
        // Two providers (and two orderings) must agree: the embedding runner batches in pages of 32, so a
        // vector that depended on its position in the batch would corrupt every later page.
        var texts = Enumerable.Range(0, 35).Select(i => $"đoạn số {i}").ToList();

        var first = await Provider().EmbedAsync(texts);
        var second = await Provider().EmbedAsync([.. texts.AsEnumerable().Reverse()]);

        for (var index = 0; index < texts.Count; index++)
        {
            var reversedIndex = texts.Count - 1 - index;
            Assert.Equal(first[index].ToArray(), second[reversedIndex].ToArray());
        }
    }
}
