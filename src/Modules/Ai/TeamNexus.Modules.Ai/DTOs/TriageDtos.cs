namespace TeamNexus.Modules.Ai.DTOs;

/// <summary>
/// Triage request for a task that does not exist yet (Giai đoạn 14.7 §A3). Deliberately just the two fields
/// the decision needs — no board ids, no assignee, nothing the server can look up itself.
/// </summary>
public sealed record TaskTriageRequest(string Title, string? Description);

/// <summary>One ranked option inside a triage response.</summary>
/// <param name="Field">Which suggestion it belongs to: <c>domain</c>, <c>workType</c> or <c>priority</c>.</param>
/// <param name="Probability">
/// The model's probability for this value. <b>0 for a priority option</b>: priority is answered as a
/// probability-weighted <i>position</i> on a scale, not as a distribution, so a number here would be invented.
/// </param>
public sealed record TriageOptionResponse(string Field, string Value, double Probability);

/// <summary>
/// Triage outcome (Giai đoạn 14.7 §A3).
/// <para>
/// Outside <see cref="Jev.ConfidenceRouting.DirectAssign"/> every suggestion field is <b>null</b> and
/// <see cref="Options"/> carries the ranked alternatives instead — the branch is enforced server-side, so a
/// client cannot accidentally auto-fill a mid-confidence guess.
/// </para>
/// </summary>
/// <param name="Degraded">
/// True when no usable suggestion could be produced (provider unavailable, or confidence too low to offer
/// options). Lets the UI say "AI tạm không khả dụng" rather than implying the AI looked and found nothing.
/// </param>
public sealed record TaskTriageResponse(
    string? SuggestedDomain,
    string? SuggestedWorkType,
    string? SuggestedPriority,
    bool NeedsSecurityReview,
    double ConfidenceScore,
    string RoutingAdvice,
    IReadOnlyList<TriageOptionResponse> Options,
    bool Degraded,
    string Model);

/// <summary>One scored assignee candidate (Giai đoạn 14.7 §A4).</summary>
/// <param name="Reason">
/// A server-composed sentence ("Backend Dev · csharp, dotnet · 2 thẻ đang mở"). Written by us, not by the
/// model, so it can never contradict the numbers beside it.
/// </param>
public sealed record CandidateSuggestionResponse(
    Guid UserId,
    string DisplayName,
    double Probability,
    string? Reason);

/// <summary>
/// Assignee dispatching outcome (Giai đoạn 14.7 §A4). Same three-branch discipline as triage:
/// <see cref="RecommendedUserId"/> is null unless the advice is <c>DirectAssign</c>.
/// </summary>
public sealed record AssigneeSuggestionResponse(
    Guid? RecommendedUserId,
    double ConfidenceScore,
    string RoutingAdvice,
    IReadOnlyList<CandidateSuggestionResponse> Candidates,
    bool Degraded,
    string Model);
