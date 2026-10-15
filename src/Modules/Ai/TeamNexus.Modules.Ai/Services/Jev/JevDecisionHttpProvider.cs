using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Services.Jev;

namespace TeamNexus.Modules.Ai.Services.Jev;

/// <summary>
/// Real Jev transport: one <c>POST {BaseUrl}{DecisionsPath}</c> per decision (Giai đoạn 14.7 §A1).
/// <para>
/// <b>Why this is not <c>DeepSeekAiProvider</c> with a different BaseUrl.</b> The existing provider(s) all
/// speak the OpenAI-compatible <c>/chat/completions</c> shape — <c>messages</c> in, generated text out.
/// Jev exposes a Decisions surface instead: <c>state</c> + <c>questions</c> in, typed <c>answers</c> out,
/// no messages, no text, no streaming. Same host (OpenRouter), genuinely different contract.
/// </para>
/// <para>
/// Transport only: it never interprets the business meaning of an answer — that belongs to
/// <c>JevDecisionService</c> and <see cref="JevDecisionValidator"/>. Every failure surfaces as
/// <see cref="JevDecisionException"/> (→ 502), and the API key never reaches a log message or an
/// exception message.
/// </para>
/// <para>
/// <b>No retry, deliberately</b> — the same rule <c>DeepSeekAiProvider</c> follows (Phase 3 §2.2). Callers
/// of Jev are all optional-suggestion paths that degrade gracefully, so a retry would only add latency to a
/// request the user is waiting on.
/// </para>
/// </summary>
public sealed class JevDecisionHttpProvider : IJevDecisionProvider
{
    /// <summary>Error bodies are truncated before they reach a client/log (never a full HTML dump).</summary>
    internal const int ErrorBodyLimit = 500;

    /// <summary>Snippet of a malformed body kept for diagnostics.</summary>
    internal const int ParseSnippetLimit = 200;

    /// <summary>Remote schema limit on <c>session_id</c> (256 characters).</summary>
    internal const int MaxSessionIdLength = 256;

    /// <summary>Narrowest usable <c>score</c> scale (TypeSafe: "should have at least two levels").</summary>
    internal const int MinScoreLevels = 2;

    /// <summary>Widest documented <c>score</c> scale (TypeSafe: "the API accepts up to 10").</summary>
    internal const int MaxScoreLevels = 10;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly JevOptions _options;
    private readonly ILogger<JevDecisionHttpProvider> _logger;

    public JevDecisionHttpProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<JevOptions> options,
        ILogger<JevDecisionHttpProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<JevDecisionResponse> DecideAsync(
        JevDecisionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(request);

        var body = new JevDecisionsBody(
            _options.EffectiveModel,
            ParseState(request.StateJson),
            request.Questions.ToDictionary(
                q => q.Id,
                q => new JevQuestionWire(ToWireType(q.Type), q.Instructions, BuildCriteria(q)),
                StringComparer.Ordinal),
            string.IsNullOrWhiteSpace(request.SessionId) ? null : request.SessionId);

        _logger.LogDebug(
            "Jev request: model={Model}, questions={QuestionCount}, state={StateLength} chars.",
            _options.EffectiveModel,
            request.Questions.Count,
            request.StateJson.Length);

        var responseBody = await SendAsync(body, ct);
        var parsed = ReadResponse(responseBody);

        if (parsed.Usage is not null)
        {
            _logger.LogDebug(
                "Jev response: model={Model}, provider={Provider}, inputTokens={InputTokens}, "
                + "outputTokens={OutputTokens}, costUsd={CostUsd}, answers={AnswerCount}.",
                parsed.Model,
                parsed.Provider,
                parsed.Usage.InputTokens,
                parsed.Usage.OutputTokens,
                parsed.Usage.CostUsd,
                parsed.Answers?.Count ?? 0);
        }

        return parsed;
    }

    /// <summary>
    /// One POST. Every transport failure becomes <see cref="JevDecisionException"/>. The
    /// <c>OperationCanceledException</c> filter is the important subtlety: HttpClient's own timeout
    /// surfaces as a cancellation with the <b>caller's</b> token untouched, so that case is a timeout
    /// (502) while a real caller cancellation must keep propagating.
    /// </summary>
    private async Task<string> SendAsync(JevDecisionsBody body, CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, _options.DecisionsUri)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JevJson.Request),
                Encoding.UTF8,
                "application/json"),
        };

        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_options.ApiKey}");
        httpRequest.Headers.TryAddWithoutValidation("Accept", "application/json");

        HttpResponseMessage response;
        string responseBody;

        try
        {
            var client = _httpClientFactory.CreateClient(AiModule.JevHttpClientName);
            response = await client.SendAsync(httpRequest, ct);
            responseBody = await response.Content.ReadAsStringAsync(ct);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new JevDecisionException(
                $"Jev không phản hồi trong {_options.Timeout.TotalSeconds:0}s (timeout).");
        }
        catch (HttpRequestException ex)
        {
            // ex.Message is transport-level ("connection refused", DNS, TLS). It cannot contain the
            // Authorization header, so this is safe to surface.
            throw new JevDecisionException($"Không gọi được Jev: {ex.Message}");
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Jev trả về lỗi HTTP {StatusCode} (model={Model}).",
                    (int)response.StatusCode,
                    _options.EffectiveModel);

                throw new JevDecisionException(
                    $"Jev trả về {(int)response.StatusCode} {ReasonPhrase(response.StatusCode)}: "
                    + $"{DescribeError(responseBody)}");
            }

            return responseBody;
        }
    }

    /// <summary>
    /// Prefers the provider's own <c>error.message</c> over a raw body dump: OpenRouter returns a structured
    /// envelope, and passing that message through gives an operator something actionable ("Invalid request
    /// parameters") instead of 500 characters of JSON.
    /// <para>
    /// The two metadata keys that matter operationally are appended when present: <c>limit_source</c>
    /// distinguishes "out of credit" from "in-flight budget" on a 402, and <c>remedy_hint</c> is the
    /// provider's own suggestion. Without them a 402 is indistinguishable from a transient failure and the
    /// natural reaction — retry — is the wrong one.
    /// </para>
    /// </summary>
    private static string DescribeError(string body)
    {
        JevErrorEnvelope? envelope = null;

        try
        {
            envelope = JsonSerializer.Deserialize<JevErrorEnvelope>(body, JevJson.Response);
        }
        catch (JsonException)
        {
            // Not the documented envelope — fall through to the raw (truncated) body below.
        }

        if (string.IsNullOrWhiteSpace(envelope?.Error?.Message))
        {
            return Truncate(body, ErrorBodyLimit);
        }

        var description = envelope.Error.Message.Trim();

        if (envelope.Error.Code is { } code)
        {
            description += $" (code {code})";
        }

        var hint = ReadMetadata(envelope.Error.Metadata, "limit_source")
                   ?? ReadMetadata(envelope.Error.Metadata, "remedy_hint");

        return hint is null ? description : $"{description} — {hint}";
    }

    /// <summary>Reads one string out of the error metadata bag; absent/non-string ⇒ <c>null</c>.</summary>
    private static string? ReadMetadata(Dictionary<string, JsonElement>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            _ => value.GetRawText(),
        };
    }

    /// <summary>
    /// Parses one response into the validated-by-nobody raw shape. A body with no <c>answers</c> object is a
    /// provider failure rather than an empty decision: the caller cannot distinguish "no answers" from "the
    /// answer never arrived", and silently returning an empty result would look like a low-confidence
    /// decision to every caller.
    /// </summary>
    private static JevDecisionResponse ReadResponse(string body)
    {
        JevDecisionsResponse? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<JevDecisionsResponse>(body, JevJson.Response);
        }
        catch (JsonException ex)
        {
            throw new JevDecisionException(
                $"Không parse được phản hồi Jev: {ex.Message} (bắt đầu: {Truncate(body, ParseSnippetLimit)})");
        }

        if (parsed is null)
        {
            throw new JevDecisionException("Jev trả về JSON rỗng (null).");
        }

        if (parsed.Answers is null)
        {
            throw new JevDecisionException(
                $"Jev không trả về 'answers' nào (bắt đầu: {Truncate(body, ParseSnippetLimit)})");
        }

        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);

        foreach (var (id, wire) in parsed.Answers)
        {
            if (wire is null)
            {
                continue;
            }

            answers[id] = ToAnswer(wire);
        }

        return new JevDecisionResponse(
            answers,
            parsed.Model,
            parsed.Provider,
            parsed.Usage is null
                ? new JevUsage(null, null, null)
                : new JevUsage(parsed.Usage.InputTokens, parsed.Usage.OutputTokens, parsed.Usage.Cost));
    }

    /// <summary>
    /// Wire answer → domain answer. An unrecognised <c>type</c> is skipped rather than mapped to a default:
    /// guessing which primitive an unknown payload was meant to be is how a wrong label reaches a human.
    /// </summary>
    private static JevAnswer ToAnswer(JevAnswerWire wire)
    {
        var type = ParseWireType(wire.Type);

        return new JevAnswer(
            type,
            wire.Noul,
            wire.Choice,
            wire.Score,
            new Dictionary<string, double>(wire.Probabilities ?? [], StringComparer.OrdinalIgnoreCase),
            wire.Confidence,
            ToLegend(wire.Legend));
    }

    /// <summary>
    /// The <c>legend</c> object is documented as <c>{ level: string }</c>, but the schema allows an object or
    /// an array as a value too. Only string values are kept — a non-string level cannot be rendered, and
    /// flattening it to <c>ToString()</c> would put raw JSON in front of a manager.
    /// </summary>
    private static IReadOnlyDictionary<string, string> ToLegend(Dictionary<string, JsonElement>? legend)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in legend ?? [])
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                result[key] = value.GetString() ?? string.Empty;
            }
        }

        return result;
    }

    /// <summary>
    /// <c>criteria</c> → raw JSON. An object for <c>choice</c>, an array for <c>score</c>, an object for
    /// <c>noul</c>; the serializer is left to shape it so the wire stays exactly what the caller described.
    /// </summary>
    private static JsonElement? BuildCriteria(JevQuestion question)
        => question.Type switch
        {
            JevQuestionType.Choice when question.ChoiceCriteria is { Count: > 0 } choice =>
                JsonSerializer.SerializeToElement(choice, JevJson.Request),
            JevQuestionType.Score when question.ScoreCriteria is { Count: > 0 } score =>
                JsonSerializer.SerializeToElement(score, JevJson.Request),
            JevQuestionType.Noul when question.NoulCriteria is { Count: > 0 } noul =>
                JsonSerializer.SerializeToElement(noul, JevJson.Request),
            _ => null,
        };

    /// <summary>The <c>state</c> field as raw JSON so Jev receives an object, not a JSON-encoded string.</summary>
    private static JsonElement ParseState(string stateJson)
    {
        if (string.IsNullOrWhiteSpace(stateJson))
        {
            throw new JevDecisionException("State không được rỗng.");
        }

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(stateJson);
        }
        catch (JsonException ex)
        {
            // The state is built by our own caller, so this is a programming error, not a provider one.
            throw new JevDecisionException($"State không phải JSON hợp lệ: {ex.Message}");
        }
    }

    /// <summary>Fails fast so a malformed call never spends money or a round-trip.</summary>
    private void Validate(JevDecisionRequest request)
    {
        if (request.Questions.Count == 0)
        {
            throw new JevDecisionException("Questions không được rỗng.");
        }

        if (request.Questions.Count > _options.Effective.MaxQuestionsPerRequest)
        {
            throw new JevDecisionException(
                $"Tối đa {_options.Effective.MaxQuestionsPerRequest} câu hỏi mỗi lượt "
                + $"(nhận {request.Questions.Count}).");
        }

        if (request.SessionId is { Length: > MaxSessionIdLength })
        {
            // The remote schema caps session_id at 256 characters. Sending more is a 400 from the provider,
            // which would surface as "Jev is broken" when the real cause is our own correlation id.
            throw new JevDecisionException(
                $"SessionId tối đa {MaxSessionIdLength} ký tự (nhận {request.SessionId.Length}).");
        }

        foreach (var question in request.Questions)
        {
            if (string.IsNullOrWhiteSpace(question.Id))
            {
                throw new JevDecisionException("Mỗi câu hỏi phải có id.");
            }

            if (string.IsNullOrWhiteSpace(question.Instructions))
            {
                throw new JevDecisionException($"Câu hỏi '{question.Id}' thiếu instructions.");
            }

            ValidateScoreQuestion(question);
        }
    }

    /// <summary>
    /// A <c>score</c> question needs an ordered scale to be answerable, but the remote schema only enforces
    /// "at least one level" while TypeSafe documents 2–10 as the usable range. A one-level scale makes every
    /// answer identical and an over-long one drifts past what the model was trained on, so both are rejected
    /// here — before the call, not as a mysterious low-confidence result afterwards.
    /// </summary>
    private static void ValidateScoreQuestion(JevQuestion question)
    {
        if (question.Type != JevQuestionType.Score)
        {
            return;
        }

        var levels = question.ScoreCriteria?.Count ?? 0;

        if (levels is < MinScoreLevels or > MaxScoreLevels)
        {
            throw new JevDecisionException(
                $"Câu hỏi score '{question.Id}' cần {MinScoreLevels}–{MaxScoreLevels} mức "
                + $"(nhận {levels}).");
        }
    }

    private static string ToWireType(JevQuestionType type) => type switch
    {
        JevQuestionType.Choice => "choice",
        JevQuestionType.Score => "score",
        _ => "noul",
    };

    /// <summary>Wire <c>type</c> → primitive. Unknown values fall back to <see cref="JevQuestionType.Noul"/>.</summary>
    private static JevQuestionType ParseWireType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "choice" => JevQuestionType.Choice,
        "score" => JevQuestionType.Score,
        _ => JevQuestionType.Noul,
    };

    private static string ReasonPhrase(HttpStatusCode statusCode) => statusCode.ToString();

    private static string Truncate(string? value, int limit)
    {
        var text = value?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return "<empty>";
        }

        return text.Length <= limit ? text : text[..limit] + "…";
    }
}
