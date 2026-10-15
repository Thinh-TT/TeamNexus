using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TeamNexus.Modules.Ai.Services.Jev;

/// <summary>
/// Jev as <b>System 1</b> for TeamNexus (Giai đoạn 14.7 §A1, Trụ cột 1 &amp; 3): the fast, structured
/// decision maker that answers typed questions in a single pass.
/// <para>
/// Four decision points are exposed — task triage, assignee dispatching, Observer risk severity and
/// Observer recovery routing. Every one of them <b>returns a suggestion and writes nothing</b>: the
/// Accountability Layer is only ever entered further up the stack, by an explicit
/// <c>IAiActionService</c> call.
/// </para>
/// <para>
/// <b>Every member degrades instead of throwing.</b> Jev is optional assistance, never a required step, so
/// a provider failure produces an empty result with <see cref="ConfidenceRouting.ManualRequired"/> and a
/// warning log. The only exceptions are the two configuration-level ones thrown <i>before</i> any call:
/// <see cref="JevDisabledException"/> (503) and the guard failures of the provider itself.
/// </para>
/// </summary>
public interface IJevDecisionService
{
    /// <summary>Triage one task: domain + work type + priority + security flag, in one pass.</summary>
    Task<TaskTriageResult> TriageTaskAsync(TaskTriageContext context, CancellationToken ct = default);

    /// <summary>Rank assignee candidates by skill fit and current load.</summary>
    Task<AssigneeRecommendationResult> RecommendAssigneeAsync(
        AssigneeDispatchContext context, CancellationToken ct = default);

    /// <summary>Assess how severe a set of Observer signals is, and whether it needs an immediate alert.</summary>
    Task<ObserverRiskAssessmentResult> AssessObserverSignalsAsync(
        ObserverRiskContext context, CancellationToken ct = default);

    /// <summary>Choose which recovery action to propose for a High/Critical signal.</summary>
    Task<RecoveryActionDecisionResult> DecideRecoveryActionAsync(
        RecoveryActionContext context, CancellationToken ct = default);
}

/// <inheritdoc cref="IJevDecisionService" />
public sealed class JevDecisionService : IJevDecisionService
{
    // ---- question ids (stable: they are the keys of the answers dictionary) --------------------

    internal const string DomainQuestion = "domain";
    internal const string WorkTypeQuestion = "workType";
    internal const string PriorityQuestion = "priority";
    internal const string SecurityQuestion = "needsSecurityReview";
    internal const string AssigneeQuestion = "assignee";
    internal const string SeverityQuestion = "severity";
    internal const string ImmediateAlertQuestion = "immediateAlert";
    internal const string DominoRiskQuestion = "dominoRisk";
    internal const string RecoveryActionQuestion = "recoveryAction";

    /// <summary>
    /// Priority scale, ascending. Its <b>array position</b> is the score Jev returns, so the order here is
    /// the contract with the model — reordering this array silently inverts Urgent and Low.
    /// <para>
    /// Public because it is part of the wire contract rather than an implementation detail: it is the scale
    /// the model was told about, and a test must be able to pin its order.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> PriorityLevels { get; } = ["Low", "Medium", "High", "Urgent"];

    /// <summary>Noul answers above this probability count as "true".</summary>
    internal const double NoulTrueThreshold = 0.5;

    private readonly IJevDecisionProvider _provider;
    private readonly JevOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<JevDecisionService> _logger;

    public JevDecisionService(
        IJevDecisionProvider provider,
        IOptions<JevOptions> options,
        TimeProvider clock,
        ILogger<JevDecisionService> logger)
    {
        _provider = provider;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    // ---- 1. triage ----------------------------------------------------------------------------

    public async Task<TaskTriageResult> TriageTaskAsync(
        TaskTriageContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureEnabled();

        var domains = context.Domains.Count > 0 ? context.Domains : [];
        var workTypes = context.WorkTypes.Count > 0 ? context.WorkTypes : [];

        // The state carries only what the decision needs. It is built here (not by the caller) because both
        // the field names and the truncation are part of the prompt contract with the model.
        var start = Stopwatch.GetTimestamp();
        var state = BuildTaskState(context.Title, context.Description);

        var questions = new List<JevQuestion>
        {
            new(DomainQuestion, JevQuestionType.Choice,
                "Thẻ này thuộc miền kỹ thuật nào? Chọn đúng một nhãn trong danh sách.",
                ChoiceCriteria: ToCriteria(domains)),
            new(WorkTypeQuestion, JevQuestionType.Choice,
                "Thẻ này thuộc loại hình công việc nào? Chọn đúng một nhãn trong danh sách.",
                ChoiceCriteria: ToCriteria(workTypes)),
            new(PriorityQuestion, JevQuestionType.Score,
                "Mức độ gấp của thẻ này theo thang từ thấp đến cao.",
                ScoreCriteria: [.. PriorityLevels]),
            new(SecurityQuestion, JevQuestionType.Noul,
                "Thẻ này có cần được rà soát bảo mật không (dữ liệu nhạy cảm, xác thực, phân quyền, "
                + "thanh toán, upload file)?",
                NoulCriteria: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["true"] = "Có chạm tới dữ liệu nhạy cảm, xác thực, phân quyền, thanh toán hoặc upload file.",
                    ["false"] = "Luồng nghiệp vụ thông thường, không chạm tới các vùng trên.",
                }),
        };

        var decision = await RunAsync(questions, state, ct);
        var answers = JevDecisionValidator.NormalizeAnswers(decision);
        var confidence = ConfidenceOf(answers, DomainQuestion);
        var advice = ConfidenceRouting.For(confidence, _options.Effective);
        var latencyMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;

        if (advice == ConfidenceRouting.ManualRequired)
        {
            LogDegraded("TriageTask", confidence, Stopwatch.GetElapsedTime(start));
            return EmptyTriageResult(advice, latencyMs, ToUsage(decision));
        }

        var domain = ResolveChoice(answers, DomainQuestion, domains);
        var workType = ResolveChoice(answers, WorkTypeQuestion, workTypes);
        var priority = ResolvePriority(answers, PriorityQuestion);
        var needsSecurity = ResolveNoul(answers, SecurityQuestion);

        // Outside DirectAssign the single value must NOT be presented as "the" answer: the Options list is
        // the whole message, and leaving a field pre-filled would defeat the point of the middle branch.
        var options = advice == ConfidenceRouting.DirectAssign
            ? []
            : BuildOptions(answers, priority);

        var result = new TaskTriageResult(
            advice == ConfidenceRouting.DirectAssign ? domain : null,
            advice == ConfidenceRouting.DirectAssign ? workType : null,
            advice == ConfidenceRouting.DirectAssign ? priority : null,
            needsSecurity,
            confidence,
            advice,
            options,
            latencyMs,
            ToUsage(decision));

        _logger.LogDebug(
            "Jev triage: advice={Advice}, confidence={Confidence}, latencyMs={LatencyMs}, "
            + "domain={Domain}, workType={WorkType}, priority={Priority}, security={Security}.",
            advice, confidence, latencyMs,
            result.SuggestedDomain, result.SuggestedWorkType, result.SuggestedPriority, needsSecurity);

        return result;
    }

    // ---- 2. assignee dispatching --------------------------------------------------------------

    public async Task<AssigneeRecommendationResult> RecommendAssigneeAsync(
        AssigneeDispatchContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        EnsureEnabled();

        var candidates = context.Candidates
            .Take(Math.Max(1, _options.Effective.MaxCandidates))
            .ToList();

        if (candidates.Count == 0)
        {
            return new AssigneeRecommendationResult(null, 0d, [], ConfidenceRouting.ManualRequired);
        }

        var start = Stopwatch.GetTimestamp();
        var state = BuildDispatchState(context, candidates);

        // The criteria keys are the server-issued handles ("cand-1", …). Jev therefore never sees — and can
        // never return — a user id; the label -> Guid mapping lives only in this stack frame.
        var criteria = candidates.ToDictionary(
            candidate => candidate.Label,
            candidate => DescribeCandidate(candidate),
            StringComparer.Ordinal);

        var questions = new List<JevQuestion>
        {
            new(AssigneeQuestion, JevQuestionType.Choice,
                "Ai là người phù hợp nhất để nhận thẻ này, xét cả kỹ năng và khối lượng công việc hiện tại? "
                + "Chọn đúng một mã ứng viên.",
                ChoiceCriteria: criteria),
        };

        var decision = await RunAsync(questions, state, ct);
        var answers = JevDecisionValidator.NormalizeAnswers(decision);
        var confidence = ConfidenceOf(answers, AssigneeQuestion);
        var advice = ConfidenceRouting.For(confidence, _options.Effective);
        var latencyMs = (long)Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var usage = ToUsage(decision);

        if (advice == ConfidenceRouting.ManualRequired)
        {
            LogDegraded("RecommendAssignee", confidence, Stopwatch.GetElapsedTime(start));
            return new AssigneeRecommendationResult(null, confidence, [], advice, latencyMs, usage);
        }

        if (!answers.TryGetValue(AssigneeQuestion, out var answer)
            || !JevDecisionValidator.IsUsable(answer, questions[0]))
        {
            LogDegraded("RecommendAssignee", confidence, Stopwatch.GetElapsedTime(start));
            return new AssigneeRecommendationResult(null, confidence, [], ConfidenceRouting.ManualRequired, latencyMs, usage);
        }

        var ranked = Rank(answers[AssigneeQuestion], candidates);
        var found = candidates.FirstOrDefault(c =>
            string.Equals(c.Label, answers[AssigneeQuestion].Choice, StringComparison.OrdinalIgnoreCase));

        if (found is null)
        {
            // Defensive: the validator already proved the label came from the offered set, so this can only
            // mean the criteria and the candidate list disagreed — a programming error worth surfacing loudly
            // in the log rather than rendering as an empty suggestion.
            _logger.LogWarning(
                "Jev chọn nhãn '{Label}' không có trong danh sách ứng viên đã gửi.", answers[AssigneeQuestion].Choice);

            return new AssigneeRecommendationResult(null, confidence, [], ConfidenceRouting.ManualRequired, latencyMs, usage);
        }

        // A high-confidence answer still renders its runner-up ranking: the manager keeps the counter-argument
        // without the system forcing a single name (the roadmap's "góc nhìn phản biện").
        var result = new AssigneeRecommendationResult(found.UserId, confidence, ranked, advice, latencyMs, usage);

        _logger.LogDebug(
            "Jev assignee: advice={Advice}, confidence={Confidence}, latencyMs={LatencyMs}, candidates={Count}.",
            advice, confidence, latencyMs, ranked.Count);

        return result;
    }

    // ---- 3. Observer severity -----------------------------------------------------------------

    public async Task<ObserverRiskAssessmentResult> AssessObserverSignalsAsync(
        ObserverRiskContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Deliberately NOT EnsureEnabled(): the Observer path is gated by its own config flag
        // (Observer:JevSeverityRouting) so the timer cannot start throwing when Jev is switched off. The
        // caller checks its flag and falls back to the LLM path.
        var start = Stopwatch.GetTimestamp();
        var state = BuildObserverState(context);

        var questions = new List<JevQuestion>
        {
            new(SeverityQuestion, JevQuestionType.Score,
                "Mức độ nghiêm trọng tổng thể của tình hình tiến độ này.",
                ScoreCriteria: [ObserverSeverity.Low, ObserverSeverity.Medium, ObserverSeverity.High, ObserverSeverity.Critical]),
            new(ImmediateAlertQuestion, JevQuestionType.Noul,
                "Tình hình này có cần phát cảnh báo khẩn ngay cho quản lý không?",
                NoulCriteria: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["true"] = "Cần báo ngay: đang chặn tiến độ hoặc sắp đổ vỡ cam kết.",
                    ["false"] = "Chưa cần báo khẩn: có thể xử lý trong nhịp làm việc bình thường.",
                }),
            new(DominoRiskQuestion, JevQuestionType.Noul,
                "Nếu để nguyên, tình hình này có nguy cơ kéo theo đổ vỡ dây chuyền của cả sprint không?",
                NoulCriteria: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["true"] = "Có nguy cơ lan sang các thẻ hoặc mốc cam kết khác.",
                    ["false"] = "Ảnh hưởng khu trú ở một thẻ hoặc một người.",
                }),
        };

        var decision = await RunAsync(questions, state, ct);
        var answers = JevDecisionValidator.NormalizeAnswers(decision);
        var confidence = ConfidenceOf(answers, SeverityQuestion);
        var advice = ConfidenceRouting.For(confidence, _options.Effective);

        var severity = advice == ConfidenceRouting.ManualRequired
            ? ObserverSeverity.Medium
            : ResolveSeverity(answers, SeverityQuestion);

        var result = new ObserverRiskAssessmentResult(
            severity,
            advice == ConfidenceRouting.ManualRequired || ResolveNoul(answers, ImmediateAlertQuestion),
            advice != ConfidenceRouting.ManualRequired && ResolveNoul(answers, DominoRiskQuestion),
            confidence,
            advice,
            ToUsage(decision));

        _logger.LogDebug(
            "Jev observer risk: advice={Advice}, confidence={Confidence}, latencyMs={LatencyMs}, severity={Severity}, "
            + "alert={Alert}, domino={Domino}.",
            advice, confidence, Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            severity, result.RequireImmediateAlert, result.HasDominoRisk);

        return result;
    }

    // ---- 4. recovery routing ------------------------------------------------------------------

    public async Task<RecoveryActionDecisionResult> DecideRecoveryActionAsync(
        RecoveryActionContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var options = context.Options.Count > 0 ? context.Options : [];
        if (options.Count == 0)
        {
            return new RecoveryActionDecisionResult(null, null, null, 0d, ConfidenceRouting.ManualRequired);
        }

        var start = Stopwatch.GetTimestamp();
        var state = BuildRecoveryState(context);

        var criteria = options
            .GroupBy(option => option.Kind, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Description,
                StringComparer.Ordinal);

        var questions = new List<JevQuestion>
        {
            new(RecoveryActionQuestion, JevQuestionType.Choice,
                "Kịch bản khắc phục nào nên được đề xuất cho tình hình này? Chọn đúng một phương án.",
                ChoiceCriteria: criteria),
        };

        var decision = await RunAsync(questions, state, ct);
        var answers = JevDecisionValidator.NormalizeAnswers(decision);
        var confidence = ConfidenceOf(answers, RecoveryActionQuestion);
        var advice = ConfidenceRouting.For(confidence, _options.Effective);
        var usage = ToUsage(decision);

        if (advice == ConfidenceRouting.ManualRequired)
        {
            LogDegraded("DecideRecoveryAction", confidence, Stopwatch.GetElapsedTime(start));
            return new RecoveryActionDecisionResult(null, null, null, confidence, advice, usage);
        }

        if (!answers.TryGetValue(RecoveryActionQuestion, out var answer)
            || !JevDecisionValidator.IsUsable(answer, questions[0]))
        {
            LogDegraded("DecideRecoveryAction", confidence, Stopwatch.GetElapsedTime(start));
            return new RecoveryActionDecisionResult(null, null, null, confidence, ConfidenceRouting.ManualRequired, usage);
        }

        // Only the action KIND comes from Jev. Which row it applies to is decided by our own caller, because
        // Jev never received an id and therefore cannot be wrong about one.
        var kind = options
            .Select(option => option.Kind)
            .FirstOrDefault(candidate =>
                string.Equals(candidate, answer.Choice, StringComparison.OrdinalIgnoreCase));

        return new RecoveryActionDecisionResult(kind, null, null, confidence, advice, usage);
    }

    // ---- shared plumbing ----------------------------------------------------------------------

    /// <summary>
    /// One provider call with the degradation contract applied uniformly: any
    /// <see cref="JevDecisionException"/> from the provider becomes an empty decision plus a warning, so no
    /// caller needs its own try/catch and no caller can accidentally turn "AI unavailable" into a 500.
    /// </summary>
    private async Task<JevDecisionResponse?> RunAsync(
        IReadOnlyList<JevQuestion> questions, string stateJson, CancellationToken ct)
    {
        try
        {
            return await _provider.DecideAsync(new JevDecisionRequest(questions, stateJson), ct);
        }
        catch (JevDecisionException ex)
        {
            _logger.LogWarning(ex, "Jev không trả về quyết định; trả về kết quả rỗng (suy giảm an toàn).");
            return null;
        }
    }

    /// <summary>503 <b>before</b> any work: a disabled feature must not reveal data or spend a token.</summary>
    private void EnsureEnabled()
    {
        if (!_options.Enabled)
        {
            throw new JevDisabledException();
        }
    }

    /// <summary>
    /// The provider's usage block as a domain value (Giai đoạn 14.7 Track A, ISSUE-01/ISSUE-05).
    /// Returns <c>null</c> when there was no decision at all, so "we never reached the provider" stays
    /// distinguishable from "the provider reported no usage".
    /// </summary>
    private static JevDecisionUsage? ToUsage(JevDecisionResponse? decision)
        => decision is null
            ? null
            : new JevDecisionUsage(
                decision.Usage.InputTokens,
                decision.Usage.OutputTokens,
                decision.Usage.CostUsd);

    private void LogDegraded(string operation, double confidence, TimeSpan elapsed)
        => _logger.LogInformation(
            "Jev {Operation}: không có đề xuất dùng được (advice=ManualRequired, confidence={Confidence}, "
            + "latencyMs={LatencyMs}) — người dùng tự quyết định.",
            operation, confidence, elapsed.TotalMilliseconds);

    /// <summary>
    /// The degraded triage result: nothing offered, and the usage carried through so a caller can still see
    /// what the failed attempt cost (Jev bills input tokens whether or not the answer was usable).
    /// </summary>
    private static TaskTriageResult EmptyTriageResult(
        string advice, long latencyMs = 0, JevDecisionUsage? usage = null)
        => new(null, null, null, false, 0d, advice, [], latencyMs, usage);

    /// <summary>
    /// Confidence of the decision as a whole. Read from the question that carries the routing weight
    /// (<c>domain</c> for triage, <c>assignee</c> for dispatch, <c>severity</c> for risk) — the other
    /// questions refine the answer, they do not decide which branch the UI takes.
    /// </summary>
    private static double ConfidenceOf(
        IReadOnlyDictionary<string, JevAnswer> answers, string questionId)
    {
        if (!answers.TryGetValue(questionId, out var answer) || answer.Confidence is not { } confidence)
        {
            // A `noul`-only or malformed answer has no confidence of its own. Returning 0 sends it to the
            // manual branch, which is the safe reading: we cannot claim a confidence we were not given.
            return 0d;
        }

        return double.IsFinite(confidence) ? Math.Clamp(confidence, 0d, 1d) : 0d;
    }

    /// <summary>
    /// Reads a <c>choice</c> answer and maps its label back to the server's own key. Matching is
    /// case-insensitive and checked against the offered set, so a label that differs only in casing still
    /// resolves instead of silently becoming "no suggestion".
    /// </summary>
    private static string? ResolveChoice(
        IReadOnlyDictionary<string, JevAnswer> answers, string questionId, IReadOnlyList<TaxonomyEntry> offered)
    {
        if (!answers.TryGetValue(questionId, out var answer) || string.IsNullOrWhiteSpace(answer.Choice))
        {
            return null;
        }

        return offered
            .FirstOrDefault(entry => string.Equals(entry.Key, answer.Choice, StringComparison.OrdinalIgnoreCase)
                                     || string.Equals(entry.Label, answer.Choice, StringComparison.OrdinalIgnoreCase))
            ?.Key;
    }

    /// <summary>
    /// Reads a <c>score</c> answer as a priority. The score is a probability-weighted position on
    /// <see cref="PriorityLevels"/>, so it is rounded to the nearest level and clamped — a 3.99 is Urgent,
    /// not an out-of-range index.
    /// </summary>
    private static string? ResolvePriority(IReadOnlyDictionary<string, JevAnswer> answers, string questionId)
    {
        if (!answers.TryGetValue(questionId, out var answer)
            || answer.Score is not { } score
            || !double.IsFinite(score))
        {
            return null;
        }

        var index = (int)Math.Round(score, MidpointRounding.AwayFromZero);
        index = Math.Clamp(index, 0, PriorityLevels.Count - 1);

        return PriorityLevels[index];
    }

    /// <summary>Reads a <c>score</c> answer as one of the four Observer severities.</summary>
    private static string ResolveSeverity(IReadOnlyDictionary<string, JevAnswer> answers, string questionId)
    {
        if (!answers.TryGetValue(questionId, out var answer)
            || answer.Score is not { } score
            || !double.IsFinite(score))
        {
            return ObserverSeverity.Medium;
        }

        var index = (int)Math.Round(score, MidpointRounding.AwayFromZero);
        index = Math.Clamp(index, 0, ObserverSeverity.All.Count - 1);

        return ObserverSeverity.All[index];
    }

    /// <summary>Reads a <c>noul</c> answer as a boolean at <see cref="NoulTrueThreshold"/>.</summary>
    private static bool ResolveNoul(IReadOnlyDictionary<string, JevAnswer> answers, string questionId)
        => answers.TryGetValue(questionId, out var answer)
           && answer.Noul is { } probability
           && double.IsFinite(probability)
           && probability >= NoulTrueThreshold;

    /// <summary>
    /// Builds the ranked option list for the middle band. Sorted by probability, capped at
    /// <see cref="ConfidenceRouting.MaxOfferedCandidates"/>, and never empty when a value was resolved — an
    /// empty middle band would render as "no suggestion" while the advice says otherwise.
    /// </summary>
    private static List<TriageOption> BuildOptions(
        IReadOnlyDictionary<string, JevAnswer> answers, string? priority)
    {
        var options = new List<TriageOption>();

        AddChoices(options, answers, DomainQuestion, "domain");
        AddChoices(options, answers, WorkTypeQuestion, "workType");

        if (priority is not null)
        {
            // Priority is a `score`, so the provider reports a position rather than a distribution. The
            // resolved level is carried with probability 0 (meaning "no distribution behind it") instead of
            // a fabricated number — a made-up 0.7 here would be indistinguishable from a real measurement.
            options.Add(new TriageOption("priority", priority, 0d));
        }

        return options;
    }

    private static void AddChoices(
        List<TriageOption> target, IReadOnlyDictionary<string, JevAnswer> answers, string questionId, string field)
    {
        if (!answers.TryGetValue(questionId, out var answer) || answer.Probabilities.Count == 0)
        {
            return;
        }

        var ranked = answer.Probabilities
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(ConfidenceRouting.MaxOfferedCandidates);

        foreach (var (value, probability) in ranked)
        {
            target.Add(new TriageOption(field, value, probability));
        }
    }

    /// <summary>Ranks candidates by the answer's probability distribution and attaches the human reason.</summary>
    private static List<CandidateScore> Rank(
        JevAnswer answer, IReadOnlyList<MemberWorkloadContext> candidates)
    {
        var byLabel = candidates.ToDictionary(
            candidate => candidate.Label, candidate => candidate, StringComparer.OrdinalIgnoreCase);

        return answer.Probabilities
            .Where(pair => byLabel.ContainsKey(pair.Key))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .Take(ConfidenceRouting.MaxOfferedCandidates)
            .Select(pair =>
            {
                var candidate = byLabel[pair.Key];
                return new CandidateScore(candidate.UserId, candidate.DisplayName, pair.Value, DescribeCandidate(candidate));
            })
            .ToList();
    }

    /// <summary>
    /// The sentence a manager reads next to a candidate. Written by us (not by Jev) so the explanation can
    /// never contradict the numbers beside it — and so it stays in Vietnamese with the right diacritics.
    /// </summary>
    private static string DescribeCandidate(MemberWorkloadContext candidate)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(candidate.PrimaryRole))
        {
            parts.Add(candidate.PrimaryRole.Trim());
        }

        if (candidate.Skills.Count > 0)
        {
            parts.Add(string.Join(", ", candidate.Skills.Take(6)));
        }

        var load = $"{candidate.OpenTaskCount} thẻ đang mở";
        if (candidate.OverdueCount > 0)
        {
            load += $", {candidate.OverdueCount} quá hạn";
        }

        if (candidate.MaxWipLimit > 0)
        {
            load += $" (WIP tối đa {candidate.MaxWipLimit})";
        }

        parts.Add(load);

        return string.Join(" · ", parts);
    }

    private static Dictionary<string, string> ToCriteria(IReadOnlyList<TaxonomyEntry> entries)
        => entries
            .GroupBy(entry => entry.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => string.IsNullOrWhiteSpace(group.First().Label) ? group.Key : group.First().Label,
                StringComparer.Ordinal);

    // ---- state builders (bounded, camelCase, no ids) -------------------------------------------

    /// <summary>
    /// Task state. Both fields are truncated to <c>Jev:MaxStateChars</c> <b>before</b> the request is built,
    /// so an oversized description can never inflate the bill (Jev bills input tokens) or overflow the 32k
    /// window. The title gets a smaller share: a title is short by schema (200 chars), so spending half the
    /// budget on it would only starve the description, which is where the decision signal actually lives.
    /// </summary>
    private string BuildTaskState(string title, string? description)
    {
        const int TitleShare = 500;
        var titleLimit = Math.Clamp(_options.Effective.MaxStateChars / 4, 100, TitleShare);
        var truncatedTitle = Truncate(title, titleLimit);
        var descriptionLimit = Math.Max(0, _options.Effective.MaxStateChars - (truncatedTitle?.Length ?? 0));

        return JevJson.SerializeState(new
        {
            title = truncatedTitle,
            description = Truncate(description, descriptionLimit),
        });
    }

    private string BuildDispatchState(
        AssigneeDispatchContext context, IReadOnlyList<MemberWorkloadContext> candidates)
        => JevJson.SerializeState(new
        {
            task = new
            {
                title = Truncate(context.TaskTitle, 400),
                description = Truncate(context.TaskDescription, Math.Max(0, _options.Effective.MaxStateChars / 2)),
            },
            candidates = candidates.Select(DescribeCandidateState).ToList(),
        });

    /// <summary>
    /// One candidate as the model sees it. <b>No email, no user id</b> — the decision needs skill, role and
    /// load, and none of those require identifying information.
    /// </summary>
    private static object DescribeCandidateState(MemberWorkloadContext candidate) => new
    {
        label = candidate.Label,
        role = candidate.PrimaryRole,
        skills = candidate.Skills,
        capacityWeight = candidate.CapacityWeight,
        maxWipLimit = candidate.MaxWipLimit,
        openTasks = candidate.OpenTaskCount,
        overdueTasks = candidate.OverdueCount,
        inProgressTasks = candidate.InProgressCount,
    };

    private string BuildObserverState(ObserverRiskContext context)
        => JevJson.SerializeState(new
        {
            workspace = Truncate(context.WorkspaceName, 200),
            boardCount = context.BoardCount,
            openTasks = context.OpenTaskCount,
            overdueTasks = context.OverdueTaskCount,
            stalledTasks = context.StalledTaskCount,
            atRiskTasks = context.AtRiskTaskCount,
            overloadedMembers = context.OverloadedMemberCount,
            signals = context.Signals.Select(signal => new
            {
                type = signal.Type,
                detectedSeverity = signal.Severity,
                weight = signal.Weight,
                summary = Truncate(signal.Summary, 300),
                taskCount = signal.TaskCount,
                userCount = signal.UserCount,
            }).ToList(),
        });

    private string BuildRecoveryState(RecoveryActionContext context)
        => JevJson.SerializeState(new
        {
            task = new
            {
                title = Truncate(context.TaskTitle, 400),
                description = Truncate(context.TaskDescription, 1_000),
            },
            signalType = context.SignalType,
            severity = context.EvaluatedSeverity,
            daysOverdue = context.DaysOverdue,
            daysRemaining = context.DaysRemaining,
            assigneeOpenTasks = context.OpenTaskCountOfAssignee,
            candidateAssigneeCount = context.CandidateAssigneeCount,
        });

    /// <summary>Character-level truncation. Null-safe, and never returns a longer string than the limit.</summary>
    private static string? Truncate(string? value, int limit)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (limit <= 0)
        {
            return null;
        }

        return value.Length <= limit ? value : value[..limit];
    }
}
