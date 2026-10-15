namespace TeamNexus.Modules.Ai.Services.Jev;

/// <summary>
/// How a Jev result should be presented, derived <b>by our own code</b> from the confidence score
/// (Giai đoạn 14.7 §A1, Trụ cột 3).
/// <para>
/// The whole point of this type is that the branch is decided <b>after</b> the answer arrives, in our
/// code, from a threshold we control — never by the provider and never by the client. A model that
/// reports 0.99 for a coin flip still lands in <see cref="DirectAssign"/>, but a model that reports 0.30
/// can never cause an auto-fill no matter how it shapes its response.
/// </para>
/// </summary>
public static class ConfidenceRouting
{
    /// <summary>One high-confidence suggestion: safe to auto-fill behind a single "Apply" click.</summary>
    public const string DirectAssign = "DirectAssign";

    /// <summary>Confidence in the middle band: offer the top 2–3 options and let a human pick.</summary>
    public const string OfferChoices = "OfferChoices";

    /// <summary>Confidence below the floor, or an unusable answer: do not guess — ask a human.</summary>
    public const string ManualRequired = "ManualRequired";

    /// <summary>The low-confidence path for an analysis task: hand it to System 2 (the LLM) to ask a clarifying question.</summary>
    public const string AwaitingClarification = "AwaitingClarification";

    /// <summary>At most this many candidates are offered in the middle band (roadmap: "Top 2-3").</summary>
    public const int MaxOfferedCandidates = 3;

    /// <summary>
    /// Maps one confidence score to its branch. Boundaries are <b>inclusive lower</b>:
    /// <c>≥ high</c> ⇒ <see cref="DirectAssign"/>; <c>≥ medium</c> ⇒ <see cref="OfferChoices"/>;
    /// otherwise <see cref="ManualRequired"/>.
    /// <para>
    /// A non-finite score (NaN from a malformed payload) is treated as <b>manual</b>. Every comparison
    /// against NaN is false, so without this guard NaN would silently fall through to the last branch —
    /// which happens to be correct here, but relying on that is exactly the kind of accident that breaks
    /// when the branch order changes.
    /// </para>
    /// </summary>
    public static string For(double confidence, JevEffectiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (double.IsNaN(confidence) || double.IsInfinity(confidence))
        {
            return ManualRequired;
        }

        if (confidence >= options.HighConfidenceThreshold)
        {
            return DirectAssign;
        }

        return confidence >= options.MediumConfidenceThreshold ? OfferChoices : ManualRequired;
    }

    /// <summary>True when the result may be auto-filled (i.e. exactly the single-suggestion branch).</summary>
    public static bool AllowsAutoFill(string? routingAdvice)
        => string.Equals(routingAdvice, DirectAssign, StringComparison.Ordinal);
}

/// <summary>
/// Cross-checks a raw Jev answer against the questions the server actually asked (Giai đoạn 14.7 §A1).
/// <para>
/// This is the anti-hallucination boundary for the System One path. It is deliberately a <b>pure static
/// function</b> with no I/O: the rules are the interesting part, and they need to be verifiable with a
/// literal rather than a provider double.
/// </para>
/// <para>
/// Unlike the Observer's validator, a violation here does <b>not</b> produce an error response. Every Jev
/// caller is an optional-suggestion path, so the correct reaction to an unusable answer is "offer nothing",
/// not "fail the request". Callers read <c>RoutingAdvice == ManualRequired</c> and degrade.
/// </para>
/// </summary>
public static class JevDecisionValidator
{
    /// <summary>Tolerance on the summed probabilities of a <c>choice</c> answer.</summary>
    private const double ProbabilitySumTolerance = 0.1;

    /// <summary>
    /// Matches a canonical 8-4-4-4-12 GUID/UUID. Jev cannot return one by construction — every label is
    /// issued by us — so this is defence in depth: if a future provider ever echoes a database id into a
    /// label or legend, it must never reach a prompt or a screen.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex GuidPattern = new(
        @"\b[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\b",
        System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Validates one answer against its question. Returns <c>false</c> when the answer must be discarded:
    /// unknown type, missing required field, out-of-range probability, a label the server never offered, a
    /// legend level outside the offered scale, or an identifier leak.
    /// </summary>
    public static bool IsUsable(JevAnswer? answer, JevQuestion question)
    {
        if (answer is null || question is null || answer.Type != question.Type)
        {
            return false;
        }

        return question.Type switch
        {
            JevQuestionType.Noul => IsUsableNoul(answer),
            JevQuestionType.Choice => IsUsableChoice(answer, question),
            JevQuestionType.Score => IsUsableScore(answer, question),
            _ => false,
        };
    }

    /// <summary>A <c>noul</c> answer is usable when its probability is a real number inside <c>[0, 1]</c>.</summary>
    private static bool IsUsableNoul(JevAnswer answer)
        => answer.Noul is { } probability
           && IsUnitInterval(probability)
           && !ContainsIdentifier(answer.Probabilities.Keys);

    /// <summary>
    /// A <c>choice</c> answer is usable when it names one of the labels the server offered, every
    /// probability is in <c>[0, 1]</c>, the probabilities sum to roughly 1, and every probability key is a
    /// label the server offered — a stray key would otherwise reach the UI as an invented option.
    /// </summary>
    private static bool IsUsableChoice(JevAnswer answer, JevQuestion question)
    {
        var offered = question.ChoiceCriteria?.Keys.ToList() ?? [];
        if (offered.Count == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(answer.Choice)
            || !offered.Contains(answer.Choice, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (ContainsIdentifier([answer.Choice, .. answer.Probabilities.Keys]))
        {
            return false;
        }

        if (answer.Probabilities.Count == 0)
        {
            // A choice with no distribution cannot be branched on: the whole point of the primitive is the
            // probability mass. Treat it as unusable rather than inventing a 1.0 for the winner.
            return false;
        }

        foreach (var (label, probability) in answer.Probabilities)
        {
            if (!IsUnitInterval(probability))
            {
                return false;
            }

            if (!offered.Contains(label, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        var total = answer.Probabilities.Values.Sum();
        return Math.Abs(total - 1d) <= ProbabilitySumTolerance;
    }

    /// <summary>
    /// A <c>score</c> answer is usable when it sits inside the offered scale and every probability key is a
    /// level of that scale. The scale index is derived from the <b>server's</b> array position, not from
    /// whatever numbering the provider chose.
    /// </summary>
    private static bool IsUsableScore(JevAnswer answer, JevQuestion question)
    {
        var levels = question.ScoreCriteria ?? [];
        if (levels.Count == 0 || answer.Score is not { } score || !double.IsFinite(score))
        {
            return false;
        }

        if (score < 0d || score > levels.Count - 1)
        {
            return false;
        }

        if (answer.Probabilities.Count == 0)
        {
            return false;
        }

        if (ContainsIdentifier([.. answer.Probabilities.Keys, .. answer.Legend.Values, .. answer.Legend.Keys]))
        {
            return false;
        }

        foreach (var (level, probability) in answer.Probabilities)
        {
            if (!IsUnitInterval(probability) || !IsScaleKey(level, levels.Count))
            {
                return false;
            }
        }

        var total = answer.Probabilities.Values.Sum();
        return Math.Abs(total - 1d) <= ProbabilitySumTolerance;
    }

    /// <summary>True when <paramref name="key"/> is an integer index inside <c>[0, count)</c>.</summary>
    private static bool IsScaleKey(string key, int count)
        => int.TryParse(key, System.Globalization.NumberStyles.Integer,
               System.Globalization.CultureInfo.InvariantCulture, out var index)
           && index >= 0
           && index < count;

    private static bool IsUnitInterval(double value)
        => double.IsFinite(value) && value is >= 0d and <= 1d;

    /// <summary>True when any value looks like a database identifier (see <see cref="GuidPattern"/>).</summary>
    private static bool ContainsIdentifier(IEnumerable<string> values)
        => values.Any(value => !string.IsNullOrEmpty(value) && GuidPattern.IsMatch(value));

    /// <summary>
    /// Indexes an answer dictionary by question id, case-insensitively, and drops a <c>null</c> answer.
    /// Providers echo the id we sent, but a casing difference must not silently turn a good answer into
    /// "no answer".
    /// </summary>
    public static IReadOnlyDictionary<string, JevAnswer> NormalizeAnswers(JevDecisionResponse? response)
    {
        var normalized = new Dictionary<string, JevAnswer>(StringComparer.OrdinalIgnoreCase);

        foreach (var (id, answer) in response?.Answers ?? new Dictionary<string, JevAnswer>(StringComparer.Ordinal))
        {
            if (answer is not null)
            {
                normalized[id] = answer;
            }
        }

        return normalized;
    }
}
