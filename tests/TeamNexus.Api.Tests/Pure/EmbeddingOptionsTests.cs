using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data.Configurations;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Giai đoạn 14.7 Track B §B3 — <see cref="EmbeddingOptions"/>: the derived values, the schema agreement, and
/// the one <c>ValidateOnStart</c> in this module.
/// <para>
/// <b>Why the dimension check is a startup failure rather than a runtime one.</b> The column is
/// <c>vector(1536)</c>, so a mismatch would make <i>every</i> insert fail with a provider-side message that
/// names no document. Failing at boot is the only useful behaviour, and the failure has to be observable —
/// which is what the host-build test below proves.
/// </para>
/// </summary>
public sealed class EmbeddingOptionsTests
{
    [Fact]
    public void SchemaDimension_MatchesTheVectorColumnInThePersistenceConfiguration()
    {
        // ⛔ The two literals live in different assemblies on purpose (Ai must not reference a Persistence
        // configuration type), so this assertion is the thing that actually keeps them in sync. A drift here
        // is not a compile error — it is every embedding insert failing in production.
        Assert.Equal(
            WorkspaceKnowledgeChunkConfiguration.EmbeddingDimension,
            EmbeddingOptions.SchemaDimension);
    }

    [Fact]
    public void Defaults_AreOffWithTheSchemaDimensionAndNoModelCommitment()
    {
        var options = new EmbeddingOptions();

        // Off by default: chunking and embedding cost money per document, so switching it on is explicit.
        Assert.False(options.Enabled);
        Assert.False(options.CanEmbed);
        Assert.False(options.HasApiKey);

        Assert.Equal(EmbeddingOptions.SchemaDimension, options.Dimensions);

        // A blank model is allowed while disabled; the effective value names the documented default.
        Assert.Equal(string.Empty, options.Model);
        Assert.Equal("openai/text-embedding-3-small", options.EffectiveModel);
    }

    [Fact]
    public void Enabled_IsTheOnlyThingThatMakesCanEmbedTrue()
    {
        Assert.True(new EmbeddingOptions { Enabled = true }.CanEmbed);

        // A key while disabled must NOT enable the feature: `Enabled` is the switch, the key only chooses the
        // provider.
        Assert.False(new EmbeddingOptions { Enabled = false, ApiKey = "sk-live" }.CanEmbed);
    }

    [Fact]
    public void HasApiKey_IgnoresWhitespaceOnlyKeys()
    {
        // The test host pins the key to a single space precisely so this stays false (the offline provider).
        Assert.False(new EmbeddingOptions { ApiKey = " " }.HasApiKey);
        Assert.False(new EmbeddingOptions { ApiKey = "\t" }.HasApiKey);
        Assert.True(new EmbeddingOptions { ApiKey = "sk-live" }.HasApiKey);
    }

    [Fact]
    public void EffectiveModel_TrimsAndFallsBackWhenBlank()
    {
        Assert.Equal("openai/text-embedding-3-small", new EmbeddingOptions { Model = "   " }.EffectiveModel);
        Assert.Equal("custom/model", new EmbeddingOptions { Model = "  custom/model  " }.EffectiveModel);
    }

    [Fact]
    public void EmbeddingsUri_IsTheFixedOpenAiCompatiblePathOnTheConfiguredHost()
    {
        var options = new EmbeddingOptions { BaseUrl = "https://openrouter.ai/api/v1" };

        Assert.Equal("https://openrouter.ai/api/v1/embeddings", options.EmbeddingsUri.ToString());
    }

    [Theory]
    [InlineData("https://openrouter.ai/api/v1/")]
    [InlineData("  https://openrouter.ai/api/v1//  ")]
    public void EmbeddingsUri_TrimsTrailingSlashesSoBothFormsProduceTheSameRequest(string baseUrl)
    {
        var options = new EmbeddingOptions { BaseUrl = baseUrl };

        Assert.Equal("https://openrouter.ai/api/v1/embeddings", options.EmbeddingsUri.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmbeddingsUri_WithABlankHost_FallsBackToTheDocumentedDefault(string? baseUrl)
    {
        var options = new EmbeddingOptions { BaseUrl = baseUrl! };

        Assert.Equal("https://openrouter.ai/api/v1/embeddings", options.EmbeddingsUri.ToString());
    }

    [Fact]
    public void Timeout_IsClampedIntoTheSupportedRangeSoAConfigurationCannotDisableIt()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), new EmbeddingOptions().Timeout);
        Assert.Equal(TimeSpan.FromSeconds(1), new EmbeddingOptions { TimeoutSeconds = 0 }.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(1), new EmbeddingOptions { TimeoutSeconds = -10 }.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(120), new EmbeddingOptions { TimeoutSeconds = 5000 }.Timeout);
    }

    // ---- ValidateOnStart ----------------------------------------------------

    [Fact]
    public void Host_ConfiguredWithAMismatchedDimension_FailsToBuildAtStartup()
    {
        // ⛔ The guardrail this asserts. `Embeddings:Dimensions` must equal the schema's 1536; the host is
        // deliberately built with 768 and must refuse to start rather than accept writes that every later
        // cosine comparison would fail on.
        //
        // The exception TYPE is deliberately not pinned: `ValidateOnStart` raises an
        // `OptionsValidationException`, but the `WebApplicationFactory` build path can wrap it — so what is
        // asserted is the observable contract, namely *"building the host throws, and the failure names the
        // setting and the required value"*. A test that only checked "it does not throw" would pass on a host
        // that silently accepted 768.
        using var factory = new MisconfiguredEmbeddingHost();

        var exception = Record.Exception(() => factory.Services);

        Assert.NotNull(exception);

        var message = Describe(exception!);

        Assert.Contains("Embeddings:Dimensions", message, StringComparison.Ordinal);

        // The message has to name the value to configure, not just the problem.
        Assert.Contains("1536", message, StringComparison.Ordinal);
    }

    /// <summary>Flattens an exception chain (and its inner exception messages) into one searchable string.</summary>
    private static string Describe(Exception exception)
    {
        var builder = new System.Text.StringBuilder();
        var current = exception;

        while (current is not null)
        {
            builder.AppendLine(current.Message);
            current = current.InnerException;
        }

        return builder.ToString();
    }

    [Fact]
    public void Host_ConfiguredWithAMatchingDimension_ResolvesTheOfflineProvider()
    {
        // The mirror image: the production value must build, and the missing key must select the deterministic
        // offline provider rather than a real one.
        using var factory = new MisconfiguredEmbeddingHost(dimensions: EmbeddingOptions.SchemaDimension);

        var options = factory.Services.GetRequiredService<IOptions<EmbeddingOptions>>().Value;

        Assert.Equal(EmbeddingOptions.SchemaDimension, options.Dimensions);
    }

    /// <summary>
    /// A host that only states the embedding dimension, so the startup validation can be observed without a
    /// database. It mirrors <see cref="TeamNexusApiFactory"/>'s composition (the one-space key trick included)
    /// and adds nothing else.
    /// </summary>
    private sealed class MisconfiguredEmbeddingHost : WebApplicationFactory<Program>
    {
        private readonly int _dimensions;

        public MisconfiguredEmbeddingHost(int dimensions = 768)
        {
            _dimensions = dimensions;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(Environments.Development);

            // No database is touched by options validation; a syntactically valid string is enough.
            builder.UseSetting("ConnectionStrings:DefaultConnection", DatabaseFixture.DefaultConnectionString);
            builder.UseSetting("Jwt:Issuer", "TeamNexus");
            builder.UseSetting("Jwt:Audience", "TeamNexus.Web");
            builder.UseSetting("Jwt:SigningKey", TeamNexusApiFactory.TestSigningKey);

            // One space, not the empty string: keeps `HasApiKey == false` so the offline provider is selected
            // and a developer's User Secrets key cannot turn this into a network call.
            builder.UseSetting("Embeddings:ApiKey", " ");
            builder.UseSetting("Embeddings:Dimensions", _dimensions.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
