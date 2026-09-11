using System.Text.Json;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Agent.Agents;

/// <summary>
/// <c>WebSearch</c> (Phase 7 §4.4): Tavily behind the <see cref="IWebSearchProvider"/> port, or the
/// offline fake when <c>Tavily:ApiKey</c> is empty. Results are trimmed and capped before they reach
/// the model (<c>Agent:MaxToolResultChars</c>), which is the main token-cost lever of this tool.
/// </summary>
public sealed class WebSearchTool : IAgentTool
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IWebSearchProvider _search;
    private readonly AgentEffectiveOptions _options;

    public WebSearchTool(IWebSearchProvider search, IOptions<AgentOptions> options)
    {
        _search = search;
        _options = options.Value.Effective;
    }

    public string Name => AgentToolDefinitions.WebSearchName;

    public async Task<string> ExecuteAsync(JsonElement arguments, AgentToolContext context, CancellationToken ct)
    {
        var query = ToolArguments.GetTrimmedString(arguments, "query");
        if (query is null)
        {
            return AgentToolRegistry.Error("'query' is required and must not be empty.");
        }

        var requested = ToolArguments.GetInt(arguments, "maxResults") ?? Math.Min(3, _options.WebSearchMaxResults);
        var maxResults = Math.Clamp(requested, 1, _options.WebSearchMaxResults);

        var results = await _search.SearchAsync(query, maxResults, ct);

        var json = JsonSerializer.Serialize(new
        {
            query,
            count = results.Count,
            results = results.Select(r => new { title = r.Title, url = r.Url, snippet = r.Snippet }),
        }, Json);

        if (json.Length > _options.MaxToolResultChars)
        {
            // Degrade rather than truncate: hand back fewer hits so the payload stays valid JSON.
            json = JsonSerializer.Serialize(new
            {
                query,
                count = 0,
                results = results.Take(Math.Max(1, results.Count / 2)).Select(r => new
                {
                    title = r.Title,
                    url = r.Url,
                    snippet = r.Snippet.Length <= 200 ? r.Snippet : r.Snippet[..200],
                }),
            }, Json);
        }

        if (json.Length > _options.MaxToolResultChars)
        {
            json = JsonSerializer.Serialize(new { query, count = results.Count, note = "Kết quả quá dài, đã lược bỏ." }, Json);
        }

        return json;
    }
}
