namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// Embedding configuration for the semantic knowledge base (Giai đoạn 14.7 Track B §B3) — section
/// <c>"Embeddings"</c>.
/// <para>
/// <b>Deliberately a separate section from <c>Jev</c>.</b> Two independent reasons: the endpoint is different
/// (<c>/api/v1/embeddings</c>, not the Decisions surface), and the embedding provider need not be the same
/// vendor at all. Sharing one section would couple two unrelated URLs and make switching embedders a change to
/// the decision-making configuration.
/// </para>
/// <para>
/// <b><see cref="Enabled"/> defaults to <c>false</c></b> (the <c>Digest:Enabled</c> precedent): chunking and
/// embedding cost money per document, so switching it on is an explicit decision. With it off, chunks are
/// still written and simply stay <c>pending</c>.
/// </para>
/// <para>
/// <b><see cref="Dimensions"/> is validated, not merely read.</b> The <c>vector(1536)</c> column is fixed in
/// the schema, so a mismatch would make every insert fail at runtime with a provider-side error. The check
/// therefore runs at startup (<c>ValidateOnStart</c>) — the one place in this module where failing fast beats
/// degrading — and the message names the migration to run.
/// </para>
/// </summary>
public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>
    /// Dimension the schema's <c>vector</c> column was created with
    /// (<c>WorkspaceKnowledgeChunkConfiguration.EmbeddingDimension</c>).
    /// <para>
    /// Duplicated as a literal here rather than referenced, because Ai must not depend on a Persistence
    /// configuration type — and a test asserts the two agree, which is what actually keeps them in sync.
    /// </para>
    /// </summary>
    public const int SchemaDimension = 1_536;

    /// <summary>Master switch. <c>false</c> ⇒ chunks stay <c>pending</c>, nothing is sent anywhere.</summary>
    public bool Enabled { get; set; }

    /// <summary>Secret. Empty ⇒ the deterministic offline <c>FakeEmbeddingProvider</c>.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>OpenAI-compatible embeddings base URL — the provider is replaceable by configuration alone.</summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai/api/v1";

    /// <summary>Model id. Empty is allowed only while <see cref="Enabled"/> is false.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Must equal <see cref="SchemaDimension"/>; enforced at startup.</summary>
    public int Dimensions { get; set; } = SchemaDimension;

    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>True when a usable key is configured → the real provider instead of the offline fake.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 120));

    /// <summary>Model id, falling back to the documented default when blank.</summary>
    public string EffectiveModel =>
        string.IsNullOrWhiteSpace(Model) ? "openai/text-embedding-3-small" : Model.Trim();

    /// <summary>
    /// Absolute embeddings URL, built from the base plus the fixed OpenAI-compatible path. Trailing slashes are
    /// trimmed so a configured value with or without one produces the same request.
    /// </summary>
    public Uri EmbeddingsUri
    {
        get
        {
            var host = (BaseUrl ?? string.Empty).Trim().TrimEnd('/');

            if (host.Length == 0)
            {
                host = "https://openrouter.ai/api/v1";
            }

            return new Uri(host + "/embeddings", UriKind.Absolute);
        }
    }

    /// <summary>
    /// Operations that call an embedder at all. Used by the caller to skip work entirely rather than to call
    /// the provider and throw: a disabled feature must not spend a token to discover it is disabled.
    /// </summary>
    public bool CanEmbed => Enabled;
}
