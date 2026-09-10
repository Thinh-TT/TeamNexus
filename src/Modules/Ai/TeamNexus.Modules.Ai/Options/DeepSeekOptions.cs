namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// DeepSeek API configuration (section "DeepSeek" in appsettings / User Secrets).
/// Follows the <c>JwtOptions</c> pattern: a plain POCO bound from configuration.
/// <para>
/// <c>ApiKey</c> intentionally has no validation at startup (no <c>ValidateOnStart</c>):
/// an empty key is a valid dev configuration — the module then falls back to
/// <c>FakeAiProvider</c> (Phase 3 §2.3) so the flow works offline at zero cost.
/// </para>
/// </summary>
public sealed class DeepSeekOptions
{
    public const string SectionName = "DeepSeek";

    /// <summary>Secret. Set via User Secrets — never committed, never logged.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    public string Model { get; set; } = "deepseek-chat";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Upper bound on generated tokens (cost control, tech docs §2.3).</summary>
    public int MaxTokens { get; set; } = 4096;

    /// <summary>Kept low on purpose: Smart Setup needs deterministic JSON, not creativity.</summary>
    public double Temperature { get; set; } = 0.2;

    /// <summary>Hard cap on how many sub-tasks one proposal may contain.</summary>
    public int MaxTaskCount { get; set; } = 20;

    /// <summary>True when a usable key is configured → real provider instead of the fake one.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public TimeSpan Timeout => TimeSpan.FromSeconds(TimeoutSeconds);

    /// <summary>
    /// Base URL without a trailing slash, ready to be concatenated with a path
    /// (e.g. <c>$"{NormalizedBaseUrl}/chat/completions"</c> — Phase 3 §2.2).
    /// Falls back to the default host when the configured value is blank.
    /// </summary>
    public string NormalizedBaseUrl
    {
        get
        {
            var trimmed = BaseUrl?.Trim().TrimEnd('/');
            return string.IsNullOrEmpty(trimmed) ? "https://api.deepseek.com" : trimmed;
        }
    }
}
