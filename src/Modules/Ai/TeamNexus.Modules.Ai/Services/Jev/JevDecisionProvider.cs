using System.Runtime.CompilerServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Jev;

/// <summary>
/// Provider-agnostic port for one Jev decision call (Giai đoạn 14.7 §A1).
/// <para>
/// Deliberately <b>separate</b> from <see cref="IAiProvider"/> rather than a fourth method on it. The
/// existing three ports all speak the OpenAI-compatible <c>/chat/completions</c> shape; Jev speaks the
/// Decisions shape (<c>state</c> + <c>questions</c> → typed <c>answers</c>, no messages, no text). Adding
/// a member would force every implementation and every test double to change at once and would make the
/// Smart Setup / Observer / Agent call sites compile against a provider that cannot serve them — the same
/// reasoning that produced <see cref="IAiToolCallingProvider"/> (Phase 7) and
/// <see cref="IAiStreamingProvider"/> (Phase 14).
/// </para>
/// <para>
/// Implementations map every transport/parse failure to <see cref="JevDecisionException"/> (→ 502) and
/// never let the API key reach a log message or an exception message.
/// </para>
/// </summary>
public interface IJevDecisionProvider
{
    /// <summary>Runs one single-pass decision. There is deliberately no retry (the <c>DeepSeekAiProvider</c> precedent).</summary>
    Task<JevDecisionResponse> DecideAsync(JevDecisionRequest request, CancellationToken ct = default);
}

/// <summary>
/// Offline stand-in for <see cref="JevDecisionHttpProvider"/>, registered whenever <c>Jev:ApiKey</c> is
/// empty. It answers with <b>zero network calls and zero tokens</b>, deterministically, so the entire
/// Jev pipeline — confidence branching, validators, Observer routing, recovery plans — is verifiable
/// offline exactly like <c>FakeAiProvider</c> makes Smart Setup and the Observer verifiable.
/// <para>
/// The scenario is selected by a <c>FAKE:JEV_*</c> sentinel found anywhere inside the <b>state</b> JSON,
/// mirroring <c>AgentMarkers</c>. Scanning the whole state rather than one named field is deliberate: the
/// three decision contexts put the task title in different places (top level for triage, nested under
/// <c>task</c> for dispatch and recovery), and a field-targeted read would silently stop working for two of
/// them — a failure mode that shows up as a mysteriously confident answer, not as an error. The trade-off is
/// that a state containing the literal sentinel text triggers the scenario; acceptable because the fake is
/// only ever registered when no API key is configured, and the strings are obviously synthetic.
/// </para>
/// <para>
/// It always re-uses the very labels the caller supplied in
/// <see cref="JevQuestion.ChoiceCriteria"/> / <see cref="JevQuestion.ScoreCriteria"/>, so a fake answer can
/// never be "valid by luck" or fail the caller's own validator for a reason the harness did not choose.
/// </para>
/// </summary>
public sealed class FakeJevDecisionProvider : IJevDecisionProvider
{
    // ---- sentinels (mirroring AgentMarkers, Phase 7 §4.5) -------------------------------------

    /// <summary>Confidence high enough to take the single-suggestion branch.</summary>
    public const string MarkerHigh = "FAKE:JEV_HIGH";

    /// <summary>Confidence in the middle band ⇒ the top-2/3 options branch.</summary>
    public const string MarkerMedium = "FAKE:JEV_MED";

    /// <summary>Confidence below the floor ⇒ the fallback branch.</summary>
    public const string MarkerLow = "FAKE:JEV_LOW";

    /// <summary>Throw <see cref="JevDecisionException"/> instead of answering (the degrade path).</summary>
    public const string MarkerError = "FAKE:JEV_ERROR";

    /// <summary>Delay before answering, so a wall-clock budget can be exercised.</summary>
    public const string MarkerSlow = "FAKE:JEV_SLOW";

    /// <summary>How long <see cref="MarkerSlow"/> stalls, long enough for a short test timeout to fire.</summary>
    public static readonly TimeSpan SlowDelay = TimeSpan.FromSeconds(3);

    private const double HighConfidence = 0.92;

    private const double MediumConfidence = 0.68;

    private const double LowConfidence = 0.30;

    private readonly ILogger<FakeJevDecisionProvider> _logger;

    public FakeJevDecisionProvider(ILogger<FakeJevDecisionProvider> logger)
    {
        _logger = logger;
    }

    /// <summary>How many times a decision was asked for — lets a suite prove a guard short-circuited (0 calls).</summary>
    public int CallCount { get; private set; }

    public async Task<JevDecisionResponse> DecideAsync(
        JevDecisionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        CallCount++;

        var sentinel = request.StateJson ?? string.Empty;

        if (sentinel.Contains(MarkerSlow, StringComparison.OrdinalIgnoreCase))
        {
            await Task.Delay(SlowDelay, ct);
        }

        if (sentinel.Contains(MarkerError, StringComparison.OrdinalIgnoreCase))
        {
            throw new JevDecisionException(
                "FakeJevDecisionProvider: kịch bản " + MarkerError + " (lỗi provider để kiểm tra nhánh suy giảm).");
        }

        var confidence = sentinel.Contains(MarkerLow, StringComparison.OrdinalIgnoreCase)
            ? LowConfidence
            : sentinel.Contains(MarkerMedium, StringComparison.OrdinalIgnoreCase)
                ? MediumConfidence
                : HighConfidence;

        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);

        foreach (var question in request.Questions)
        {
            answers[question.Id] = AnswerFor(question, confidence);
        }

        _logger.LogDebug(
            "FakeJevDecisionProvider: trả {Count} câu trả lời (confidence={Confidence}) — không gọi mạng.",
            answers.Count,
            confidence);

        // Synthetic-but-deterministic token accounting, so cost/latency plumbing is still exercised
        // offline. Deliberately NOT zero: a suite that asserts "usage was recorded" must be able to pass.
        return new JevDecisionResponse(
            answers,
            "fake-jev",
            "FakeProvider",
            new JevUsage(
                InputTokens: Math.Max(1, (request.StateJson?.Length ?? 0) / 4),
                OutputTokens: Math.Max(1, answers.Count),
                CostUsd: 0d));
    }

    /// <summary>
    /// Builds a well-formed answer for one question <b>from the question's own criteria</b>. This is the
    /// same trick <c>FakeAiProvider.BuildObserverFindings</c> uses: re-use what the server sent, so the
    /// sample always survives server-side validation regardless of the fixture.
    /// </summary>
    private static JevAnswer AnswerFor(JevQuestion question, double confidence)
    {
        switch (question.Type)
        {
            case JevQuestionType.Noul:
                return new JevAnswer(
                    JevQuestionType.Noul,
                    Noul: Math.Clamp(confidence, 0d, 1d),
                    Choice: null,
                    Score: null,
                    Probabilities: new Dictionary<string, double>(StringComparer.Ordinal),
                    Confidence: null,
                    Legend: new Dictionary<string, string>(StringComparer.Ordinal));

            case JevQuestionType.Score:
            {
                // The score scale is the caller's array; pick the top level on a confident answer and the
                // middle one otherwise, so both the "one clear answer" and the "split answer" rendering are
                // reachable offline.
                var levels = question.ScoreCriteria ?? [];
                var count = Math.Max(1, levels.Count);
                var chosen = confidence >= HighConfidence ? count - 1 : count / 2;

                var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
                var legend = new Dictionary<string, string>(StringComparer.Ordinal);

                for (var index = 0; index < count; index++)
                {
                    var key = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    probabilities[key] = index == chosen ? Round(confidence) : 0d;

                    if (index < levels.Count)
                    {
                        legend[key] = levels[index];
                    }
                }

                return new JevAnswer(
                    JevQuestionType.Score,
                    Noul: null,
                    Choice: null,
                    Score: chosen,
                    Probabilities: probabilities,
                    Confidence: Round(confidence),
                    Legend: legend);
            }

            default:
            {
                var labels = (question.ChoiceCriteria ?? new Dictionary<string, string>(StringComparer.Ordinal))
                    .Keys
                    .ToList();

                if (labels.Count == 0)
                {
                    // A choice question with no labels cannot be answered: emit an empty-but-typed answer
                    // rather than inventing a label the caller never offered (the validator would reject it
                    // anyway, and a fake that produces invalid output hides real bugs).
                    return new JevAnswer(
                        JevQuestionType.Choice,
                        Noul: null,
                        Choice: null,
                        Score: null,
                        Probabilities: new Dictionary<string, double>(StringComparer.Ordinal),
                        Confidence: null,
                        Legend: new Dictionary<string, string>(StringComparer.Ordinal));
                }

                var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
                var remaining = Math.Max(0d, 1d - confidence);
                var tail = labels.Count > 1 ? remaining / (labels.Count - 1) : 0d;

                for (var index = 0; index < labels.Count; index++)
                {
                    probabilities[labels[index]] = index == 0 ? Round(confidence) : Round(tail);
                }

                return new JevAnswer(
                    JevQuestionType.Choice,
                    Noul: null,
                    Choice: labels[0],
                    Score: null,
                    Probabilities: probabilities,
                    Confidence: Round(confidence),
                    Legend: new Dictionary<string, string>(StringComparer.Ordinal));
            }
        }
    }

    /// <summary>Rounds to 6 decimals so a JSON round-trip and a direct comparison agree.</summary>
    private static double Round(double value) => Math.Round(value, 6, MidpointRounding.ToZero);
}

/// <summary>
/// Serialization helpers shared by the fake and the real provider, so both shape the wire identically.
/// </summary>
internal static class JevJson
{
    /// <summary>
    /// camelCase, and <b>null properties are omitted</b>: this is what drops <c>sessionId</c> when unused
    /// and keeps the body to exactly what the caller asked for.
    /// </summary>
    internal static readonly JsonSerializerOptions Request = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Response body: web defaults (case-insensitive), with the two snake_case usage keys mapped explicitly.</summary>
    internal static readonly JsonSerializerOptions Response = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Serializes a state object without escaping non-ASCII. Vietnamese task titles are the normal case
    /// here, and \uXXXX escaping would inflate the prompt (and therefore the bill) for every request.
    /// </summary>
    internal static string SerializeState(object state)
        => JsonSerializer.Serialize(state, StateOptions);

    private static readonly JsonSerializerOptions StateOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}
