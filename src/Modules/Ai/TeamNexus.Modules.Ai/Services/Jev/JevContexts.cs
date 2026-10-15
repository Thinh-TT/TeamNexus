namespace TeamNexus.Modules.Ai.Services.Jev;

/// <summary>
/// Inputs the caller assembles for <see cref="IJevDecisionService"/> (Giai đoạn 14.7 §A1).
/// <para>
/// Every type here is a <b>plain record with no EF Core and no Board/Task entity</b>, on purpose: the
/// service layer must not be able to reach the database, which is what keeps the
/// <c>Ai -&gt; Board</c> one-way invariant intact and keeps every rule verifiable with a literal.
/// </para>
/// </summary>
public sealed record TaskTriageContext(
    string Title,
    string? Description,
    IReadOnlyList<TaxonomyEntry> Domains,
    IReadOnlyList<TaxonomyEntry> WorkTypes);

/// <summary>One choice option offered to Jev: the server-owned <paramref name="Key"/> is what comes back.</summary>
public sealed record TaxonomyEntry(string Key, string Label);

/// <summary>One assignee candidate. The <see cref="Label"/> is a server-issued opaque handle (e.g. <c>cand-1</c>).</summary>
public sealed record MemberWorkloadContext(
    string Label,
    Guid UserId,
    string DisplayName,
    string? PrimaryRole,
    IReadOnlyList<string> Skills,
    double CapacityWeight,
    int MaxWipLimit,
    int OpenTaskCount,
    int OverdueCount,
    int InProgressCount);

public sealed record AssigneeDispatchContext(
    string TaskTitle,
    string? TaskDescription,
    IReadOnlyList<MemberWorkloadContext> Candidates);

/// <summary>One detected Observer signal as Jev sees it (evidence ids are deliberately absent — see the class remarks).</summary>
public sealed record ObserverSignalContext(
    string Type,
    string Severity,
    int Weight,
    string Summary,
    int TaskCount,
    int UserCount);

/// <summary>
/// Everything one Observer risk assessment needs. The evidence <b>ids</b> are deliberately not carried:
/// Jev decides severity from the shape of the situation, and because it never receives an id it can never
/// echo one back. Mapping a recommendation to concrete rows stays in our code, where the id space lives.
/// </summary>
public sealed record ObserverRiskContext(
    string WorkspaceName,
    int OpenTaskCount,
    int OverdueTaskCount,
    int StalledTaskCount,
    int AtRiskTaskCount,
    int OverloadedMemberCount,
    int BoardCount,
    IReadOnlyList<ObserverSignalContext> Signals);

/// <summary>
/// One candidate recovery action. <see cref="Kind"/> is the machine value (<c>ReassignTask</c>, …) and the
/// human sentence is built from <see cref="Description"/>, so a wording change never becomes a behaviour change.
/// </summary>
public sealed record RecoveryActionOption(string Kind, string Description);

public sealed record RecoveryActionContext(
    string TaskTitle,
    string? TaskDescription,
    string SignalType,
    string EvaluatedSeverity,
    int DaysOverdue,
    int DaysRemaining,
    int OpenTaskCountOfAssignee,
    int CandidateAssigneeCount,
    IReadOnlyList<RecoveryActionOption> Options);

// ---------------------------------------------------------------------------------------------
// Results. Every one carries confidenceScore AND routingAdvice so no caller has to re-derive the branch.
//
// Giai đoạn 14.7 Track A (ISSUE-01/ISSUE-05): every result also carries `Usage`, read straight from the
// provider's own `usage` block. It belongs on the RESULT rather than being recovered later for two reasons:
// (a) Jev bills per INPUT token with output free, so "what did this decision cost" is genuinely part of the
// decision; (b) without it the number is computed and then dropped, which is exactly what made it impossible
// to show a measured cost per Observer scan.
// ---------------------------------------------------------------------------------------------

/// <summary>
/// Usage and cost of the decision that produced a result, copied from the provider's <c>usage</c> block.
/// All members are nullable: not every provider reports them (the offline fake reports synthetic ones).
/// </summary>
public sealed record JevDecisionUsage(int? InputTokens, int? OutputTokens, double? CostUsd);

/// <summary>
/// Triage of one task (Giai đoạn 14.7 §A2.1). All suggestion fields are <b>null</b> outside the
/// <see cref="ConfidenceRouting.OfferChoices"/>/<see cref="ConfidenceRouting.DirectAssign"/> branches.
/// </summary>
public sealed record TaskTriageResult(
    string? SuggestedDomain,
    string? SuggestedWorkType,
    string? SuggestedPriority,
    bool NeedsSecurityReview,
    double ConfidenceScore,
    string RoutingAdvice,
    IReadOnlyList<TriageOption> Options,
    long LatencyMs = 0,
    JevDecisionUsage? Usage = null);

/// <summary>One ranked option inside a triage result: which field, which value, how likely.</summary>
public sealed record TriageOption(string Field, string Value, double Probability);

/// <summary>One scored assignee candidate, ready for rendering. <see cref="UserId"/> is a real id and never appears in prose.</summary>
public sealed record CandidateScore(
    Guid UserId,
    string DisplayName,
    double Probability,
    string? Reason);

/// <summary>Assignee dispatch outcome (Giai đoạn 14.7 §A2.2).</summary>
public sealed record AssigneeRecommendationResult(
    Guid? RecommendedUserId,
    double ConfidenceScore,
    IReadOnlyList<CandidateScore> CandidateRankings,
    string RoutingAdvice,
    long LatencyMs = 0,
    JevDecisionUsage? Usage = null);

/// <summary>Observer severity assessment (Giai đoạn 14.7 §A5 Phần A).</summary>
public sealed record ObserverRiskAssessmentResult(
    string EvaluatedSeverity,
    bool RequireImmediateAlert,
    bool HasDominoRisk,
    double ConfidenceScore,
    string RoutingAdvice,
    JevDecisionUsage? Usage = null);

/// <summary>Recovery routing decision (Giai đoạn 14.7 §A5 Phần B).</summary>
public sealed record RecoveryActionDecisionResult(
    string? ActionType,
    Guid? TargetTaskId,
    Guid? TargetUserId,
    double ConfidenceScore,
    string RoutingAdvice,
    JevDecisionUsage? Usage = null);

/// <summary>
/// Verdict of one "does this draft satisfy the task?" question (Giai đoạn 14.7 Track B §B2), used by the
/// agent tool <c>ValidateAcceptanceCriteria</c>.
/// <para>
/// <see cref="Verified"/> is <b>nullable and never fabricated</b>: the caller distinguishes "Jev says no"
/// (<c>false</c>) from "we could not ask" (<c>null</c>), because the two lead to opposite actions — the first
/// means rework, the second means proceed on your own judgement.
/// </para>
/// </summary>
public sealed record AcceptanceValidationResult(
    bool? Verified,
    double ConfidenceScore,
    string RoutingAdvice,
    JevDecisionUsage? Usage = null);
