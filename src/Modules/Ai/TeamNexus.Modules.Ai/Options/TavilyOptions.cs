namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// Tavily Search API configuration (section "Tavily" in appsettings / User Secrets), following the
/// <c>DeepSeekOptions</c> khuôn exactly: plain POCO, no <c>ValidateOnStart</c>, an empty key is a
/// valid dev configuration (the module then registers <c>FakeWebSearchProvider</c>).
/// <para>
/// <b>Auth variant.</b> Tavily has documented both an <c>api_key</c> field in the request body and
/// an <c>Authorization: Bearer</c> header. The default is <c>Bearer</c> — the current documented
/// form, and it keeps the secret out of the request body (less chance of it reaching a log). The
/// <c>Body</c> value exists so §7C can confirm the right variant against the real endpoint by
/// changing configuration only — the task doc explicitly forbids guessing this
/// (<c>phase-7 §4.3</c>: "phải xác nhận … trước khi viết prompt; không suy đoán").
/// </para>
/// </summary>
public sealed class TavilyOptions
{
    public const string SectionName = "Tavily";

    /// <summary>Auth variants understood by <c>TavilyWebSearchProvider</c>.</summary>
    public const string AuthModeBearer = "Bearer";

    public const string AuthModeBody = "Body";

    /// <summary>Secret. Set via User Secrets — never committed, never logged.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.tavily.com";

    public int TimeoutSeconds { get; set; } = 20;

    /// <summary>Tavily <c>search_depth</c>: <c>basic</c> (1 credit) or <c>advanced</c> (2 credits).</summary>
    public string SearchDepth { get; set; } = "basic";

    /// <summary><see cref="AuthModeBearer"/> (default) or <see cref="AuthModeBody"/>.</summary>
    public string AuthMode { get; set; } = AuthModeBearer;

    /// <summary>True when a usable key is configured → real provider instead of the fake one.</summary>
    public bool HasApiKey => !string.IsNullOrWhiteSpace(ApiKey);

    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 120));

    /// <summary>True when the key travels in the <c>Authorization</c> header (the default).</summary>
    public bool UseBearerAuth =>
        !string.Equals(AuthMode?.Trim(), AuthModeBody, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Base URL without a trailing slash, ready to be concatenated with a path
    /// (e.g. <c>$"{NormalizedBaseUrl}/search"</c>). Falls back to the default host when blank.
    /// </summary>
    public string NormalizedBaseUrl
    {
        get
        {
            var trimmed = BaseUrl?.Trim().TrimEnd('/');
            return string.IsNullOrEmpty(trimmed) ? "https://api.tavily.com" : trimmed;
        }
    }

    /// <summary>Tavily <c>search_depth</c> with the two documented values only.</summary>
    public string EffectiveSearchDepth =>
        string.Equals(SearchDepth?.Trim(), "advanced", StringComparison.OrdinalIgnoreCase)
            ? "advanced"
            : "basic";
}
