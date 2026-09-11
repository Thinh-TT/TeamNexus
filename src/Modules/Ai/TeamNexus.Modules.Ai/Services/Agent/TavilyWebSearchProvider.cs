using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// Tavily Search API transport (Phase 7 §4.3). Registered only when <c>Tavily:ApiKey</c> is set.
/// <para>
/// <b>Wire shape is a verified fact, not a guess.</b> The doc for this phase explicitly forbids
/// assuming Tavily's field names, so the auth variant travels as configuration
/// (<c>Tavily:AuthMode</c>) and the chosen one is verified against a stub handler <b>and</b> one real
/// call before the prompt was written (§7C). Default = <c>Bearer</c>: the currently documented
/// authentication and the one that keeps the key out of the request body.
/// </para>
/// <para>
/// The key is never logged and never embedded in an exception message.
/// </para>
/// </summary>
public sealed class TavilyWebSearchProvider : IWebSearchProvider
{
    /// <summary>Error bodies are truncated before they reach a log/client (no full HTML dumps).</summary>
    private const int ErrorBodyLimit = 300;

    private const int SnippetLimitFallback = 400;

    /// <summary>Request body: snake_case + omit nulls (<c>api_key</c> disappears in Bearer mode).</summary>
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TavilyOptions _options;
    private readonly AgentEffectiveOptions _agent;
    private readonly ILogger<TavilyWebSearchProvider> _logger;

    public TavilyWebSearchProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<TavilyOptions> options,
        IOptions<AgentOptions> agentOptions,
        ILogger<TavilyWebSearchProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _agent = agentOptions.Value.Effective;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(
        string query, int maxResults, CancellationToken ct = default)
    {
        var trimmedQuery = query?.Trim() ?? string.Empty;
        if (trimmedQuery.Length == 0)
        {
            return [];
        }

        var cappedResults = Math.Clamp(maxResults <= 0 ? _agent.WebSearchMaxResults : maxResults, 1, _agent.WebSearchMaxResults);

        var payload = new SearchRequest(
            _options.UseBearerAuth ? null : _options.ApiKey,
            trimmedQuery,
            cappedResults,
            _options.EffectiveSearchDepth);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUri())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, RequestJson),
                Encoding.UTF8,
                "application/json"),
        };

        if (_options.UseBearerAuth)
        {
            httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");
        }

        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        string body;
        try
        {
            var client = _httpClientFactory.CreateClient(AiModule.TavilyHttpClientName);
            response = await client.SendAsync(httpRequest, ct);
            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as TaskCanceledException with the caller token untouched.
            throw new AiProviderException(
                $"Tavily không phản hồi trong {_options.TimeoutSeconds}s (timeout).");
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException($"Không gọi được Tavily: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Tavily trả về lỗi HTTP {StatusCode} (authMode={AuthMode}).",
                    (int)response.StatusCode,
                    _options.UseBearerAuth ? TavilyOptions.AuthModeBearer : TavilyOptions.AuthModeBody);

                throw new AiProviderException(
                    $"Tavily trả về {(int)response.StatusCode} {response.StatusCode}: {Truncate(body, ErrorBodyLimit)}");
            }

            return ReadResults(body);
        }
    }

    private Uri BuildUri() => new($"{_options.NormalizedBaseUrl}/search", UriKind.Absolute);

    /// <summary>
    /// Defensive parse: a successful response without a usable <c>results</c> array is NOT an error —
    /// the tool result then simply says "no hits", and the loop keeps going (same rule the fake
    /// provider applies to a malformed payload).
    /// </summary>
    private IReadOnlyList<WebSearchResult> ReadResults(string body)
    {
        TavilySearchResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<TavilySearchResponse>(body, ResponseJson);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(
                ex,
                "Tavily trả về JSON không parse được ({Length} ký tự) — coi như 0 kết quả.",
                body.Length);
            return [];
        }

        if (parsed?.Results is not { Count: > 0 })
        {
            _logger.LogWarning("Tavily trả về 0 kết quả cho truy vấn (hoặc thiếu mảng results).");
            return [];
        }

        var limit = _agent.MaxToolResultChars;
        var results = new List<WebSearchResult>(parsed.Results.Count);

        foreach (var item in parsed.Results)
        {
            if (string.IsNullOrWhiteSpace(item.Url))
            {
                continue;
            }

            // Tavily exposes the body as `content`; older/newer variants have used `snippet`.
            var snippet = FirstNonEmpty(item.Content, item.Snippet) ?? string.Empty;

            results.Add(new WebSearchResult(
                Truncate(FirstNonEmpty(item.Title, item.Url) ?? item.Url, 300),
                Truncate(item.Url, 2048),
                Truncate(snippet, Math.Min(limit, Math.Max(SnippetLimitFallback, limit)))));
        }

        return results;
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string Truncate(string value, int limit)
        => value.Length <= limit ? value : value[..limit] + "…";

    // ---- wire models -------------------------------------------------------

    private sealed record SearchRequest(
        string? ApiKey,
        string Query,
        int MaxResults,
        string SearchDepth);

    private sealed record TavilySearchResponse(List<TavilyResult>? Results);

    private sealed record TavilyResult(
        string? Title,
        string? Url,
        string? Content,
        string? Snippet);
}
