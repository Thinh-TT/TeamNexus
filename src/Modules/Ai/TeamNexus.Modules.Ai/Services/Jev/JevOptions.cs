namespace TeamNexus.Modules.Ai.Services.Jev;

/// <summary>
/// Jev (TypeSafe System One) configuration — section <c>"Jev"</c> in appsettings / environment variables.
/// Follows the <c>DeepSeekOptions</c>/<c>ObserverOptions</c> pattern: a plain POCO bound from
/// configuration, no <c>ValidateOnStart</c> for the secret, every threshold defensively clamped.
/// <para>
/// <b>Jev uses the same OpenRouter API key as any other OpenRouter call</b> — there is no separate
/// TypeSafe account or key. It is nevertheless a <b>separate section</b> from <c>DeepSeek</c> on purpose:
/// Jev is reached over a different route (the Decisions API, not <c>/chat/completions</c>), so sharing
/// one section would force the two unrelated URLs to move together.
/// </para>
/// <para>
/// <b>No <c>Temperature</c> and no <c>MaxTokens</c>.</b> Jev does not generate text — it returns typed
/// data — so sampling knobs would be misleading rather than merely unused.
/// </para>
/// </summary>
public sealed class JevOptions
{
    public const string SectionName = "Jev";

    /// <summary>Secret. Set via User Secrets (local) or <c>Jev__ApiKey</c> (host) — never committed, never logged.</summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>OpenRouter host. Contains no path so the surface can be switched by configuration alone.</summary>
    public string BaseUrl { get; set; } = "https://openrouter.ai";

    /// <summary>
    /// Path of the Decisions surface. <b>Deliberately a knob</b>: OpenRouter marks this route as
    /// <i>alpha</i>, so if it moves, recovery is one environment variable rather than a rebuild.
    /// The System One alternative is <c>/api/v1/systemone</c>.
    /// </summary>
    public string DecisionsPath { get; set; } = "/api/alpha/decisions";

    /// <summary>Model id. The <c>~typesafe/jev-latest</c> alias tracks the newest release.</summary>
    public string Model { get; set; } = "typesafe/jev-1.13";

    /// <summary>
    /// Lower than the LLM default on purpose. Jev answers in ~70–300 ms, so a 60 s timeout would keep a
    /// request hanging long after every caller's own budget has expired. Callers degrade to "no
    /// suggestion" rather than waiting.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Master switch for the three Jev-backed routes. <c>false</c> ⇒ 503 (reads keep working).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Confidence at or above which one suggestion is offered with a 1-click accept.</summary>
    public double HighConfidenceThreshold { get; set; } = 0.85;

    /// <summary>Confidence at or above which the top 2–3 options are offered instead of a single one.</summary>
    public double MediumConfidenceThreshold { get; set; } = 0.50;

    /// <summary>Hard cap on the serialized <c>state</c>. Jev's own window is 32 000 tokens.</summary>
    public int MaxStateChars { get; set; } = 12_000;

    /// <summary>How many members may be offered as assignee candidates in one request.</summary>
    public int MaxCandidates { get; set; } = 12;

    /// <summary>Guard against a caller assembling an unbounded question set.</summary>
    public int MaxQuestionsPerRequest { get; set; } = 6;

    /// <summary>
    /// Longest <c>session_id</c> accepted. Exposed only so the limit is visible next to the other knobs —
    /// the remote schema caps it at 256 characters and the client enforces the same value before sending.
    /// </summary>
    public int MaxSessionIdChars { get; set; } = 256;

    /// <summary>
    /// Route Observer severity assessment through Jev (Giai đoạn 14.7 §A5 Phần A). <c>false</c> restores
    /// the Phase 5/14 path (<c>IAiProvider</c> + <c>ObserverFindingValidator</c>) exactly, which is what
    /// makes this change revertible on production with one setting.
    /// </summary>
    public bool ObserverSeverityRouting { get; set; } = true;

    /// <summary>
    /// Let the Observer draft a Pending recovery plan for High/Critical signals. Defaults to
    /// <c>false</c> (the <c>Digest:Enabled</c> precedent): this is the one Jev feature that <i>creates</i>
    /// an action proposal, so enabling it is an explicit decision.
    /// </summary>
    public bool AutoRecoveryPlanEnabled { get; set; }

    /// <summary>Master switch for the semantic knowledge base (Track B §B3).</summary>
    public bool RagEnabled { get; set; }

    /// <summary>Max characters of one knowledge chunk handed to the embedder.</summary>
    public int RagChunkChars { get; set; } = 1_200;

    /// <summary>Max chunks one document may be split into (cost control — every chunk is an embed call).</summary>
    public int RagMaxChunksPerDocument { get; set; } = 200;

    /// <summary>Max characters of the retrieved knowledge block injected into an agent prompt.</summary>
    public int RagMaxContextChars { get; set; } = 3_000;

    /// <summary>
    /// Dimension of the stored <c>vector</c> column. Validated against the schema at startup — see
    /// <see cref="Effective"/> and <c>ValidateVectorDimension</c>: a mismatch would make every insert
    /// fail at runtime, so it must fail fast at boot instead.
    /// </summary>
    public int RagEmbeddingDimension { get; set; } = 1_536;

    /// <summary>True when a usable key is configured → the real OpenRouter provider instead of the fake one.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public TimeSpan Timeout => TimeSpan.FromSeconds(Effective.TimeoutSeconds);

    /// <summary>
    /// Absolute Decisions URL, built from the two independent knobs. Trailing slashes are trimmed so a
    /// configured value with or without one produces the same request.
    /// </summary>
    public Uri DecisionsUri
    {
        get
        {
            var host = (BaseUrl ?? string.Empty).Trim().TrimEnd('/');
            if (host.Length == 0)
            {
                host = "https://openrouter.ai";
            }

            var path = (DecisionsPath ?? string.Empty).Trim();
            if (path.Length == 0)
            {
                path = "/api/alpha/decisions";
            }

            if (!path.StartsWith('/'))
            {
                path = "/" + path;
            }

            return new Uri(host + path, UriKind.Absolute);
        }
    }

    /// <summary>Model id, falling back to the documented default when blank.</summary>
    public string EffectiveModel =>
        string.IsNullOrWhiteSpace(Model) ? "typesafe/jev-1.13" : Model.Trim();

    /// <summary>
    /// Snapshot with defensive clamping. Consumers read thresholds from here, never from the raw
    /// properties, so a misconfigured 0 or an inverted pair cannot silently disable the branching rules.
    /// </summary>
    public JevEffectiveOptions Effective
    {
        get
        {
            // The medium threshold is clamped first, then high must exceed it. Without this ordering an
            // inverted configuration (high 0.4, medium 0.8) would leave no "medium" band at all and the
            // 2–3 option path would be silently unreachable — the exact failure this type exists to prevent.
            var medium = Math.Clamp(MediumConfidenceThreshold, 0.05, 0.90);
            var high = Math.Clamp(HighConfidenceThreshold, medium + 0.05, 0.99);

            return new JevEffectiveOptions(
                TimeoutSeconds: Math.Clamp(TimeoutSeconds, 1, 120),
                HighConfidenceThreshold: high,
                MediumConfidenceThreshold: medium,
                MaxStateChars: Math.Clamp(MaxStateChars, 200, 60_000),
                MaxCandidates: Math.Clamp(MaxCandidates, 1, 50),
                MaxQuestionsPerRequest: Math.Clamp(MaxQuestionsPerRequest, 1, 20),
                RagChunkChars: Math.Clamp(RagChunkChars, 200, 8_000),
                RagMaxChunksPerDocument: Math.Clamp(RagMaxChunksPerDocument, 1, 2_000),
                RagMaxContextChars: Math.Clamp(RagMaxContextChars, 200, 20_000),
                RagEmbeddingDimension: Math.Clamp(RagEmbeddingDimension, 64, 8_192));
        }
    }
}

/// <summary>Immutable, clamped view of <see cref="JevOptions"/> (the <c>AgentEffectiveOptions</c> precedent).</summary>
public sealed record JevEffectiveOptions(
    int TimeoutSeconds,
    double HighConfidenceThreshold,
    double MediumConfidenceThreshold,
    int MaxStateChars,
    int MaxCandidates,
    int MaxQuestionsPerRequest,
    int RagChunkChars,
    int RagMaxChunksPerDocument,
    int RagMaxContextChars,
    int RagEmbeddingDimension);
