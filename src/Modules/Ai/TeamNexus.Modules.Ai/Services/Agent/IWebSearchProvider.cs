namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>One web search hit, already trimmed for the prompt.</summary>
public sealed record WebSearchResult(string Title, string Url, string Snippet);

/// <summary>
/// Web search port for the agent's <c>WebSearch</c> tool (Phase 7 §4.3). Declared as a port so the
/// real Tavily transport can be swapped for <see cref="FakeWebSearchProvider"/> when
/// <c>Tavily:ApiKey</c> is empty — the same offline-first approach as <see cref="IAiProvider"/>.
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>
    /// Search the web. Implementations cap/trim the snippets for the prompt and map transport
    /// failures (HTTP ≠ 2xx, timeout) to <see cref="AiProviderException"/> (502) without retrying;
    /// a malformed-but-successful body yields an empty list plus a warning instead of throwing.
    /// </summary>
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken ct = default);
}
