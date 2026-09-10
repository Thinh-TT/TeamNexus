using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// DeepSeek provider (Phase 3 §2.2). DeepSeek exposes an OpenAI-compatible
/// <c>POST /chat/completions</c>, so the payload follows the OpenAI chat schema and the
/// response is read as <c>choices[0].message.content</c> + <c>usage</c>.
/// <para>
/// Transport only: it returns the raw model content and never interprets the Smart Setup
/// schema — that parse/normalize step belongs to <c>SmartSetupService</c> (Phase 3 §4.3).
/// Every failure surfaces as <see cref="AiProviderException"/> (502) and the API key is
/// never written to a log message or an exception message.
/// </para>
/// </summary>
public sealed class DeepSeekAiProvider : IAiProvider
{
    /// <summary>Error bodies are truncated before they reach a client/log (no full HTML dumps).</summary>
    private const int ErrorBodyLimit = 500;

    /// <summary>Snippet of a malformed body kept for diagnostics.</summary>
    private const int ParseSnippetLimit = 200;

    /// <summary>Request body: snake_case + omit nulls (drops <c>response_format</c> when not in JSON mode).</summary>
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Response body: web defaults (case-insensitive); token keys are mapped explicitly.</summary>
    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DeepSeekOptions _options;
    private readonly ILogger<DeepSeekAiProvider> _logger;

    public DeepSeekAiProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<DeepSeekOptions> options,
        ILogger<DeepSeekAiProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<AiCompletionResult> CompleteAsync(
        AiCompletionRequest request,
        CancellationToken ct = default)
    {
        Validate(request);

        var payload = new ChatRequest(
            _options.Model,
            [
                new ChatMessage("system", request.SystemPrompt),
                new ChatMessage("user", request.UserPrompt),
            ],
            request.Temperature,
            request.MaxTokens,
            request.JsonMode ? new ResponseFormat("json_object") : null);

        _logger.LogDebug(
            "DeepSeek request: model={Model}, jsonMode={JsonMode}, system={SystemLength} chars, user={UserLength} chars, maxTokens={MaxTokens}.",
            _options.Model,
            request.JsonMode,
            request.SystemPrompt.Length,
            request.UserPrompt.Length,
            request.MaxTokens);

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, BuildUri())
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload, RequestJson),
                Encoding.UTF8,
                "application/json"),
        };
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        string body;
        try
        {
            var client = _httpClientFactory.CreateClient(AiModule.HttpClientName);
            response = await client.SendAsync(httpRequest, ct);

            body = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // HttpClient timeout surfaces as TaskCanceledException with the caller token untouched.
            throw new AiProviderException(
                $"DeepSeek không phản hồi trong {_options.TimeoutSeconds}s (timeout).");
        }
        catch (HttpRequestException ex)
        {
            throw new AiProviderException($"Không gọi được DeepSeek: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "DeepSeek trả về lỗi HTTP {StatusCode} (model={Model}).",
                    (int)response.StatusCode,
                    _options.Model);

                throw new AiProviderException(
                    $"DeepSeek trả về {(int)response.StatusCode} {ReasonPhrase(response.StatusCode)}: {Truncate(body, ErrorBodyLimit)}");
            }

            var completion = ReadCompletion(body);
            var content = completion.Content;

            if (completion.PromptTokens is not null || completion.CompletionTokens is not null)
            {
                _logger.LogDebug(
                    "DeepSeek response: promptTokens={PromptTokens}, completionTokens={CompletionTokens}, {Length} chars.",
                    completion.PromptTokens,
                    completion.CompletionTokens,
                    content.Length);
            }

            return completion;
        }
    }

    // ---- helpers ----------------------------------------------------------

    private Uri BuildUri() => new($"{_options.NormalizedBaseUrl}/chat/completions", UriKind.Absolute);

    /// <summary>Fails fast on invalid input so a malformed call never spends tokens.</summary>
    private static void Validate(AiCompletionRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            throw new AiProviderException("SystemPrompt không được rỗng.");
        }

        if (string.IsNullOrWhiteSpace(request.UserPrompt))
        {
            throw new AiProviderException("UserPrompt không được rỗng.");
        }

        if (request.MaxTokens <= 0)
        {
            throw new AiProviderException("MaxTokens phải lớn hơn 0.");
        }

        if (request.Temperature is < 0 or > 2)
        {
            throw new AiProviderException("Temperature phải nằm trong khoảng 0–2.");
        }
    }

    /// <summary>
    /// Single parse of the response body: <c>choices[0].message.content</c> (+ optional
    /// <c>usage</c>). Malformed JSON and empty content are both upstream failures → 502.
    /// Missing <c>usage</c> is not an error — token counts stay null.
    /// </summary>
    private static AiCompletionResult ReadCompletion(string body)
    {
        DeepSeekChatResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DeepSeekChatResponse>(body, ResponseJson);
        }
        catch (JsonException ex)
        {
            throw new AiProviderException(
                $"Không parse được phản hồi DeepSeek: {ex.Message} (bắt đầu: {Truncate(body, ParseSnippetLimit)})");
        }

        var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AiProviderException("DeepSeek trả về nội dung rỗng.");
        }

        return new AiCompletionResult(content, parsed!.Usage?.PromptTokens, parsed.Usage?.CompletionTokens);
    }

    private static string ReasonPhrase(HttpStatusCode statusCode)
        => statusCode.ToString();

    private static string Truncate(string? value, int limit)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return "<empty>";
        }

        return text.Length <= limit ? text : text[..limit] + "…";
    }

    // ---- wire models (OpenAI-compatible) ----------------------------------

    private sealed record ChatRequest(
        string Model,
        IReadOnlyList<ChatMessage> Messages,
        double Temperature,
        int MaxTokens,
        ResponseFormat? ResponseFormat);

    private sealed record ChatMessage(string Role, string Content);

    private sealed record ResponseFormat(string Type);

    private sealed record DeepSeekChatResponse(
        List<DeepSeekChoice>? Choices,
        DeepSeekUsage? Usage);

    private sealed record DeepSeekChoice(DeepSeekMessage? Message);

    private sealed record DeepSeekMessage(string? Content);

    private sealed record DeepSeekUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
