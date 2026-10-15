using System.Text.Json;
using System.Text.Json.Serialization;

namespace TeamNexus.Modules.Ai.Services.Jev;

/// <summary>
/// The three Jev primitives (Giai đoạn 14.7 §A1). Jev is a <b>System One</b> decision model: it never
/// generates text, it answers typed questions. The type a question declares fully determines the shape
/// of its answer — see <see cref="JevAnswer"/>.
/// </summary>
public enum JevQuestionType
{
    /// <summary>Yes/no judgment. The answer is the probability of "yes".</summary>
    Noul,

    /// <summary>One label out of a fixed set, with a probability per label and a confidence value.</summary>
    Choice,

    /// <summary>A position on an ordered scale, with a probability per level and a confidence value.</summary>
    Score,
}

/// <summary>
/// One typed question. <see cref="Criteria"/> carries the type-specific detail:
/// <list type="bullet">
///   <item>
///     <see cref="JevQuestionType.Choice"/> ⇒ an <b>object</b> <c>{ label: description }</c>. The remote
///     schema allows a description to be a string, object, array or null, but this client only ever sends
///     strings — a machine-readable description would be a second contract to keep in sync for no gain,
///     and the label is the only part that comes back. TypeSafe documents a 255-option ceiling.
///   </item>
///   <item>
///     <see cref="JevQuestionType.Score"/> ⇒ an <b>array</b> of level descriptions, ascending. TypeSafe
///     documents "at least two levels, up to 10"; the remote schema only enforces a minimum of one, so the
///     2–10 rule is ours to keep (see <c>JevDecisionService</c>).
///   </item>
///   <item>
///     <see cref="JevQuestionType.Noul"/> ⇒ optional, and when present it must describe <b>both</b> the
///     true and the false branch.
///   </item>
/// </list>
/// Everything the model may legitimately answer is therefore fixed by the caller, which is exactly what
/// makes <see cref="JevDecisionValidator"/> able to reject any label the server never offered.
/// </summary>
public sealed record JevQuestion(
    string Id,
    JevQuestionType Type,
    string Instructions,
    IReadOnlyDictionary<string, string>? ChoiceCriteria = null,
    IReadOnlyList<string>? ScoreCriteria = null,
    IReadOnlyDictionary<string, string>? NoulCriteria = null);

/// <summary>Token accounting and cost of one Jev call, read straight from the response <c>usage</c> block.</summary>
/// <param name="CostUsd">
/// USD cost of the call. Typed <see cref="double"/> (<b>not</b> <see cref="int"/>) on purpose: real values
/// are fractions of a cent (e.g. <c>0.000019992</c>), and truncating them to an integer would erase the very
/// measurement the phase is claiming. Null when the provider does not report it.
/// </param>
public sealed record JevUsage(int? InputTokens, int? OutputTokens, double? CostUsd);

/// <summary>Raw, unvalidated answer of one question as returned on the wire. Validated by <see cref="JevDecisionValidator"/>.</summary>
public sealed record JevAnswer(
    JevQuestionType Type,
    double? Noul,
    string? Choice,
    double? Score,
    IReadOnlyDictionary<string, double> Probabilities,
    double? Confidence,
    IReadOnlyDictionary<string, string> Legend);

/// <summary>One complete Jev decision: the answers keyed by question id, plus provider/model/usage metadata.</summary>
public sealed record JevDecisionResponse(
    IReadOnlyDictionary<string, JevAnswer> Answers,
    string? Model,
    string? Provider,
    JevUsage Usage);

/// <summary>One provider request: the state to evaluate plus every typed question in a single pass.</summary>
/// <param name="StateJson">
/// The <c>state</c> field, already serialized. Jev accepts a string, an object or an array; the caller
/// (<c>JevDecisionService</c>) builds a camelCase JSON object so the model sees named fields.
/// </param>
public sealed record JevDecisionRequest(
    IReadOnlyList<JevQuestion> Questions,
    string StateJson,
    string? SessionId = null);

// ---------------------------------------------------------------------------------------------
// Wire models. Private to the provider: they mirror the JSON exactly, so nothing above this layer
// has to know about `criteria` being an object for one primitive and an array for another.
// ---------------------------------------------------------------------------------------------

/// <summary>Body of <c>POST {BaseUrl}{DecisionsPath}</c>. Property names become snake_case via the serializer policy.</summary>
internal sealed record JevDecisionsBody(
    string Model,
    System.Text.Json.JsonElement State,
    IReadOnlyDictionary<string, JevQuestionWire> Questions,
    string? SessionId);

/// <summary>One question on the wire. <c>Criteria</c> is raw JSON because its shape depends on <c>Type</c>.</summary>
internal sealed record JevQuestionWire(
    string Type,
    string Instructions,
    System.Text.Json.JsonElement? Criteria);

internal sealed record JevDecisionsResponse(
    Dictionary<string, JevAnswerWire>? Answers,
    string? Model,
    string? Id,
    string? Provider,
    JevUsageWire? Usage);

/// <summary>
/// One answer on the wire, in the union shape Jev returns: all three primitives coexist in one
/// record, with <c>type</c> deciding which fields are populated. A discriminated-union serializer
/// would be more "correct" but would also break the moment the provider adds a field; reading the
/// union defensively keeps one unknown field from failing a whole decision.
/// <para>
/// Every "extra" field is optional in the remote schema for <c>choice</c> and <c>score</c> — only
/// <c>type</c> plus the primitive's own value (<c>noul</c>/<c>choice</c>/<c>score</c>) are required. Absence
/// therefore has to mean "unusable answer", which is exactly what <see cref="JevDecisionValidator"/> says,
/// rather than being coerced into a confident default.
/// </para>
/// </summary>
internal sealed record JevAnswerWire(
    string? Type,
    double? Noul,
    string? Choice,
    double? Score,
    Dictionary<string, double>? Probabilities,
    double? Confidence,
    Dictionary<string, JsonElement>? Legend);

internal sealed record JevUsageWire(
    [property: JsonPropertyName("input_tokens")] int? InputTokens,
    [property: JsonPropertyName("output_tokens")] int? OutputTokens,
    double? Cost);

/// <summary>
/// Error envelope of a non-2xx response: <c>{ "error": { "code": 400, "message": "…", "metadata": … } }</c>.
/// <para>
/// <c>metadata</c> is kept even though it is unstructured: it is where the actionable half of a 402/403/429
/// lives (<c>limit_source</c>, <c>remedy_hint</c>, <c>provider_code</c>, <c>error_type</c>), and dropping it
/// leaves an operator with a bare status code and no way to tell "out of credit" from "rate limited".
/// </para>
/// </summary>
internal sealed record JevErrorEnvelope(JevErrorWire? Error);

internal sealed record JevErrorWire(int? Code, string? Message, Dictionary<string, JsonElement>? Metadata);
