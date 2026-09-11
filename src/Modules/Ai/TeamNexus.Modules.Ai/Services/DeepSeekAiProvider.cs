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
/// DeepSeek provider (Phase 3 §2.2, extended in Phase 7 §4.2). DeepSeek exposes an OpenAI-compatible
/// <c>POST /chat/completions</c>, so both ports are served by the same endpoint and the same
/// payload record: <see cref="IAiProvider"/> keeps the fixed <c>[system, user]</c> shape of Phase 3/5
/// (byte-identical request: no <c>tools</c>, no <c>tool_choice</c>), while
/// <see cref="IAiToolCallingProvider"/> adds the function-calling fields and reads
/// <c>choices[0].message.tool_calls[]</c>.
/// <para>
/// Transport only: it never interprets the Smart Setup schema or the agent's tool semantics — those
/// belong to <c>SmartSetupService</c>/<c>AgentRunOrchestrator</c>. Every failure surfaces as
/// <see cref="AiProviderException"/> (502) and the API key is never written to a log message or an
/// exception message.
/// </para>
/// </summary>
public sealed class DeepSeekAiProvider : IAiProvider, IAiToolCallingProvider
{
    /// <summary>Error bodies are truncated before they reach a client/log (no full HTML dumps).</summary>
    private const int ErrorBodyLimit = 500;

    /// <summary>Snippet of a malformed body kept for diagnostics.</summary>
    private const int ParseSnippetLimit = 200;

    /// <summary>
    /// Request body: snake_case + omit nulls (drops <c>response_format</c> when not in JSON mode and
    /// drops <c>tools</c>/<c>tool_choice</c> entirely for the Phase 3/5 completion path).
    /// </summary>
    private static readonly JsonSerializerOptions RequestJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Response body: web defaults (case-insensitive); token keys are mapped explicitly.</summary>
    private static readonly JsonSerializerOptions ResponseJson = new(JsonSerializerDefaults.Web);

    /// <summary>Fallback tool schema — never fail a whole run because a static schema string is malformed.</summary>
    private static readonly JsonElement EmptyObjectSchema =
        JsonSerializer.Deserialize<JsonElement>("""{"type":"object","properties":{}}""");

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

    // ---- Phase 3/5: single completion (unchanged contract) ------------------

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
            request.JsonMode ? new ResponseFormat("json_object") : null,
            null,
            null);

        _logger.LogDebug(
            "DeepSeek request: model={Model}, jsonMode={JsonMode}, system={SystemLength} chars, user={UserLength} chars, maxTokens={MaxTokens}.",
            _options.Model,
            request.JsonMode,
            request.SystemPrompt.Length,
            request.UserPrompt.Length,
            request.MaxTokens);

        var body = await SendAsync(payload, ct);

        var completion = ReadCompletion(body);

        if (completion.PromptTokens is not null || completion.CompletionTokens is not null)
        {
            _logger.LogDebug(
                "DeepSeek response: promptTokens={PromptTokens}, completionTokens={CompletionTokens}, {Length} chars.",
                completion.PromptTokens,
                completion.CompletionTokens,
                completion.Content.Length);
        }

        return completion;
    }

    // ---- Phase 7: function calling ------------------------------------------

    public async Task<AiChatResult> ChatAsync(AiChatRequest request, CancellationToken ct = default)
    {
        ValidateChatRequest(request);

        var messages = new List<ChatMessage>
        {
            new("system", request.SystemPrompt),
        };

        foreach (var message in request.Messages)
        {
            messages.Add(new ChatMessage(
                message.Role,
                message.Content,
                message.ToolCallId,
                message.Name,
                message.ToolCalls is { Count: > 0 }
                    ? message.ToolCalls
                        .Select(t => new ChatToolCall(t.Id, "function", new ChatToolCallFunction(t.Name, t.ArgumentsJson)))
                        .ToList()
                    : null));
        }

        var payload = new ChatRequest(
            _options.Model,
            messages,
            request.Temperature,
            request.MaxTokens,
            null,
            request.Tools.Count == 0
                ? null
                : [.. request.Tools.Select(t => new ChatTool("function", new ChatFunction(t.Name, t.Description, ParseSchema(t.ParametersJsonSchema))))],
            // "auto" (not the strict/beta variant): `strict` forbids minLength/maxLength and
            // requires every property to be `required`, which would strangle the tool descriptions
            // (Phase 7 §4.2 — deliberately deferred).
            request.Tools.Count == 0 ? null : "auto");

        _logger.LogDebug(
            "DeepSeek chat request: model={Model}, messages={MessageCount}, tools={ToolCount}, maxTokens={MaxTokens}.",
            _options.Model,
            messages.Count,
            request.Tools.Count,
            request.MaxTokens);

        var body = await SendAsync(payload, ct);
        var result = ReadChatResult(body);

        _logger.LogDebug(
            "DeepSeek chat response: finishReason={FinishReason}, toolCalls={ToolCallCount}, content={ContentLength} chars, tokens={PromptTokens}/{CompletionTokens}.",
            result.FinishReason,
            result.ToolCalls.Count,
            result.Content?.Length ?? 0,
            result.PromptTokens,
            result.CompletionTokens);

        return result;
    }

    // ---- transport ----------------------------------------------------------

    /// <summary>
    /// One POST to <c>/chat/completions</c> shared by both ports. Every transport failure becomes
    /// <see cref="AiProviderException"/>; there is deliberately no retry (Phase 3 §2.2 / §4.2).
    /// </summary>
    private async Task<string> SendAsync(ChatRequest payload, CancellationToken ct)
    {
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

            return body;
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

    /// <summary>Same fast-fail rules for the chat path; <c>Messages</c> must not be empty.</summary>
    private static void ValidateChatRequest(AiChatRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            throw new AiProviderException("SystemPrompt không được rỗng.");
        }

        if (request.Messages.Count == 0)
        {
            throw new AiProviderException("Messages không được rỗng.");
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

    /// <summary>JSON Schema string → raw JSON for the wire (never throws on a bad/empty schema).</summary>
    private static JsonElement ParseSchema(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson))
        {
            return EmptyObjectSchema;
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(schemaJson);
        }
        catch (JsonException)
        {
            return EmptyObjectSchema;
        }
    }

    /// <summary>
    /// Single parse of a completion response: <c>choices[0].message.content</c> (+ optional
    /// <c>usage</c>). Malformed JSON and empty content are both upstream failures → 502.
    /// Missing <c>usage</c> is not an error — token counts stay null.
    /// </summary>
    private static AiCompletionResult ReadCompletion(string body)
    {
        var parsed = Deserialize(body);

        var content = parsed.Choices?.FirstOrDefault()?.Message?.Content;

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new AiProviderException("DeepSeek trả về nội dung rỗng.");
        }

        return new AiCompletionResult(content, parsed.Usage?.PromptTokens, parsed.Usage?.CompletionTokens);
    }

    /// <summary>
    /// Parse of a chat round. Missing <c>content</c> is fine (a tool-call turn has none); a response
    /// with neither content nor tool calls is rejected because the orchestrator has nothing to act on.
    /// </summary>
    private static AiChatResult ReadChatResult(string body)
    {
        var parsed = Deserialize(body);
        var choice = parsed.Choices?.FirstOrDefault();

        if (choice?.Message is null)
        {
            throw new AiProviderException($"DeepSeek không trả về message nào (bắt đầu: {Truncate(body, ParseSnippetLimit)})");
        }

        var toolCalls = new List<AiToolInvocation>();
        foreach (var call in choice.Message.ToolCalls ?? [])
        {
            var name = call.Function?.Name;
            if (string.IsNullOrWhiteSpace(name))
            {
                // A tool call without a name cannot be dispatched; skip it instead of failing the
                // whole run — the model still gets the chance to answer again next turn.
                continue;
            }

            toolCalls.Add(new AiToolInvocation(
                call.Id ?? string.Empty,
                name,
                call.Function?.Arguments ?? "{}"));
        }

        if (string.IsNullOrWhiteSpace(choice.Message.Content) && toolCalls.Count == 0)
        {
            throw new AiProviderException("DeepSeek trả về lượt rỗng (không content, không tool_calls).");
        }

        return new AiChatResult(
            choice.Message.Content,
            toolCalls,
            parsed.Usage?.PromptTokens,
            parsed.Usage?.CompletionTokens,
            choice.FinishReason ?? "stop");
    }

    private static DeepSeekChatResponse Deserialize(string body)
    {
        try
        {
            return JsonSerializer.Deserialize<DeepSeekChatResponse>(body, ResponseJson)
                   ?? throw new AiProviderException("DeepSeek trả về JSON rỗng (null).");
        }
        catch (JsonException ex)
        {
            throw new AiProviderException(
                $"Không parse được phản hồi DeepSeek: {ex.Message} (bắt đầu: {Truncate(body, ParseSnippetLimit)})");
        }
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
        ResponseFormat? ResponseFormat,
        IReadOnlyList<ChatTool>? Tools,
        string? ToolChoice);

    private sealed record ChatMessage(
        string Role,
        string? Content,
        string? ToolCallId = null,
        string? Name = null,
        IReadOnlyList<ChatToolCall>? ToolCalls = null);

    private sealed record ChatTool(string Type, ChatFunction Function);

    private sealed record ChatFunction(string Name, string? Description, JsonElement? Parameters);

    /// <summary>Echoed back inside an assistant message (<c>arguments</c> is a JSON *string*).</summary>
    private sealed record ChatToolCall(string? Id, string Type, ChatToolCallFunction Function);

    private sealed record ChatToolCallFunction(string Name, string? Arguments);

    private sealed record ResponseFormat(string Type);

    private sealed record DeepSeekChatResponse(
        List<DeepSeekChoice>? Choices,
        DeepSeekUsage? Usage);

    /// <summary>
    /// <c>finish_reason</c>/<c>tool_calls</c> MUST be mapped explicitly: the response is read with the
    /// WEB defaults (camelCase + case-insensitive), which matches <c>finishReason</c> but not the
    /// actual <c>finish_reason</c>. Leaving them unmapped silently produced "no tool calls" and made
    /// every function-calling turn look empty — caught by the §4 group C harness before it could
    /// reach a real provider call.
    /// </summary>
    private sealed record DeepSeekChoice(
        DeepSeekMessage? Message,
        [property: JsonPropertyName("finish_reason")] string? FinishReason);

    private sealed record DeepSeekMessage(
        string? Content,
        [property: JsonPropertyName("tool_calls")] List<DeepSeekToolCall>? ToolCalls);

    private sealed record DeepSeekToolCall(
        string? Id,
        string? Type,
        DeepSeekToolCallFunction? Function);

    private sealed record DeepSeekToolCallFunction(string? Name, string? Arguments);

    private sealed record DeepSeekUsage(
        [property: JsonPropertyName("prompt_tokens")] int? PromptTokens,
        [property: JsonPropertyName("completion_tokens")] int? CompletionTokens);
}
