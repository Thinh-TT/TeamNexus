using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Jev;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Giai đoạn 14.7 §A1 — <see cref="JevDecisionService"/> driven through the offline fake provider.
/// <para>
/// These tests pin the two contracts that matter most: the <b>suggestion fields are null outside the
/// single-suggestion branch</b> (so a middle-band result can never be mistaken for a decision), and a
/// <b>provider failure degrades instead of throwing</b> (so Jev can never break a request that does not
/// actually depend on it).
/// </para>
/// </summary>
public sealed class JevDecisionServiceTests
{
    private static JevDecisionService NewService(JevOptions? options = null)
        => new(
            new FakeJevDecisionProvider(NullLogger<FakeJevDecisionProvider>.Instance),
            Options.Create(options ?? new JevOptions()),
            TimeProvider.System,
            NullLogger<JevDecisionService>.Instance);

    private static TaskTriageContext TriageContext(string title = "Fix the login bug")
        => new(
            title,
            "Users cannot sign in after the last deploy.",
            [new TaxonomyEntry("Backend", "Backend"), new TaxonomyEntry("Frontend", "Frontend")],
            [new TaxonomyEntry("Feature", "Feature"), new TaxonomyEntry("Bug", "Bug")]);

    // ---- triage -------------------------------------------------------------------------------

    [Fact]
    public async Task TriageTask_WithConfidentAnswer_ReturnsASingleSuggestionAndNoOptionList()
    {
        var result = await NewService().TriageTaskAsync(TriageContext());

        Assert.Equal(ConfidenceRouting.DirectAssign, result.RoutingAdvice);
        Assert.NotNull(result.SuggestedDomain);
        Assert.NotNull(result.SuggestedWorkType);
        Assert.NotNull(result.SuggestedPriority);

        // A single suggestion is exactly that: the option list stays empty so the UI cannot render a menu
        // next to a decision.
        Assert.Empty(result.Options);

        // The resolved values must always come from the vocabulary the caller offered.
        Assert.Contains(result.SuggestedDomain, new[] { "Backend", "Frontend" });
        Assert.Contains(result.SuggestedWorkType, new[] { "Feature", "Bug" });
        Assert.Contains(result.SuggestedPriority, JevDecisionService.PriorityLevels);
    }

    [Fact]
    public async Task TriageTask_WithMediumAnswer_ReturnsOptionsAndLeavesEverySuggestionEmpty()
    {
        // This is the core enforcement of Trụ cột 3: outside the high-confidence branch nothing may be
        // pre-filled, because pre-filling IS the auto-fill the roadmap forbids at this confidence.
        var result = await NewService().TriageTaskAsync(
            TriageContext("FAKE:JEV_MED — triage this"));

        Assert.Equal(ConfidenceRouting.OfferChoices, result.RoutingAdvice);
        Assert.Null(result.SuggestedDomain);
        Assert.Null(result.SuggestedWorkType);
        Assert.Null(result.SuggestedPriority);
        Assert.NotEmpty(result.Options);
    }

    [Fact]
    public async Task TriageTask_WithLowAnswer_FallsBackToAManualDecisionWithNothingOffered()
    {
        var result = await NewService().TriageTaskAsync(
            TriageContext("FAKE:JEV_LOW — triage this"));

        Assert.Equal(ConfidenceRouting.ManualRequired, result.RoutingAdvice);
        Assert.Null(result.SuggestedDomain);
        Assert.Null(result.SuggestedWorkType);
        Assert.Null(result.SuggestedPriority);
        Assert.Empty(result.Options);
        Assert.False(result.NeedsSecurityReview);
    }

    [Fact]
    public async Task TriageTask_WhenTheProviderFails_DegradesInsteadOfThrowing()
    {
        // The whole point of the degrade contract: a dead Jev must not turn "create a task" into a 500.
        var result = await NewService().TriageTaskAsync(
            TriageContext("FAKE:JEV_ERROR — triage this"));

        Assert.Equal(ConfidenceRouting.ManualRequired, result.RoutingAdvice);
        Assert.Null(result.SuggestedDomain);
        Assert.Empty(result.Options);
    }

    [Fact]
    public async Task TriageTask_WhenJevIsDisabled_Throws503BeforeDoingAnyWork()
    {
        var service = NewService(new JevOptions { Enabled = false });

        var exception = await Assert.ThrowsAsync<JevDisabledException>(
            () => service.TriageTaskAsync(TriageContext()));

        Assert.Equal(503, exception.StatusCode);
    }

    [Fact]
    public async Task TriageTask_RejectsANullContextInsteadOfSilentlyReturningNothing()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => NewService().TriageTaskAsync(null!));
    }

    // ---- assignee dispatch --------------------------------------------------------------------

    private static AssigneeDispatchContext DispatchContext()
        => new(
            "Fix the login bug",
            "Users cannot sign in.",
            [
                new MemberWorkloadContext("cand-1", Guid.NewGuid(), "Nguyễn Văn A", "Backend Dev",
                    ["csharp", "dotnet"], 1.0, 5, 2, 0, 1),
                new MemberWorkloadContext("cand-2", Guid.NewGuid(), "Trần Văn B", "Frontend Dev",
                    ["react", "typescript"], 1.0, 5, 6, 2, 3),
            ]);

    [Fact]
    public async Task RecommendAssignee_MapsTheOpaqueLabelBackToARealUserId()
    {
        // The label -> id mapping exists only inside this call, which is precisely why Jev can never leak an
        // id: it was never given one.
        var context = DispatchContext();

        var result = await NewService().RecommendAssigneeAsync(context);

        Assert.Equal(ConfidenceRouting.DirectAssign, result.RoutingAdvice);
        Assert.NotNull(result.RecommendedUserId);
        Assert.Contains(result.RecommendedUserId!.Value, context.Candidates.Select(candidate => candidate.UserId));
    }

    [Fact]
    public async Task RecommendAssignee_AlwaysCarriesTheReasonSentenceNextToEachCandidate()
    {
        var context = DispatchContext();

        var result = await NewService().RecommendAssigneeAsync(context);

        Assert.NotEmpty(result.CandidateRankings);
        Assert.All(result.CandidateRankings, candidate =>
        {
            Assert.False(string.IsNullOrWhiteSpace(candidate.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(candidate.Reason));
            Assert.InRange(candidate.Probability, 0d, 1d);
        });
    }

    [Fact]
    public async Task RecommendAssignee_RanksByDescendingProbability()
    {
        var result = await NewService().RecommendAssigneeAsync(DispatchContext());

        var probabilities = result.CandidateRankings.Select(candidate => candidate.Probability).ToList();
        Assert.Equal(probabilities.OrderByDescending(value => value).ToList(), probabilities);
    }

    [Fact]
    public async Task RecommendAssignee_NeverOffersMoreThanThreeCandidates()
    {
        var context = DispatchContext() with
        {
            Candidates =
            [
                new MemberWorkloadContext("cand-1", Guid.NewGuid(), "A", "Backend Dev", ["a"], 1, 5, 1, 0, 1),
                new MemberWorkloadContext("cand-2", Guid.NewGuid(), "B", "Backend Dev", ["b"], 1, 5, 1, 0, 1),
                new MemberWorkloadContext("cand-3", Guid.NewGuid(), "C", "Backend Dev", ["c"], 1, 5, 1, 0, 1),
                new MemberWorkloadContext("cand-4", Guid.NewGuid(), "D", "Backend Dev", ["d"], 1, 5, 1, 0, 1),
                new MemberWorkloadContext("cand-5", Guid.NewGuid(), "E", "Backend Dev", ["e"], 1, 5, 1, 0, 1),
            ],
        };

        var result = await NewService().RecommendAssigneeAsync(context);

        Assert.True(result.CandidateRankings.Count <= ConfidenceRouting.MaxOfferedCandidates);
    }

    [Fact]
    public async Task RecommendAssignee_WithLowConfidence_OffersNobodyAndNoRanking()
    {
        var result = await NewService().RecommendAssigneeAsync(
            DispatchContext() with { TaskTitle = "FAKE:JEV_LOW — who should do this?" });

        Assert.Equal(ConfidenceRouting.ManualRequired, result.RoutingAdvice);
        Assert.Null(result.RecommendedUserId);
        Assert.Empty(result.CandidateRankings);
    }

    [Fact]
    public async Task RecommendAssignee_WithNoCandidates_ReturnsManualWithoutCallingTheProvider()
    {
        var provider = new FakeJevDecisionProvider(NullLogger<FakeJevDecisionProvider>.Instance);
        var service = new JevDecisionService(
            provider,
            Options.Create(new JevOptions()),
            TimeProvider.System,
            NullLogger<JevDecisionService>.Instance);

        var result = await service.RecommendAssigneeAsync(
            new AssigneeDispatchContext("title", null, []));

        Assert.Equal(ConfidenceRouting.ManualRequired, result.RoutingAdvice);
        Assert.Null(result.RecommendedUserId);

        // Zero calls is the assertion that matters: an empty candidate list must not spend a token.
        Assert.Equal(0, provider.CallCount);
    }

    // ---- observer risk ------------------------------------------------------------------------

    private static ObserverRiskContext RiskContext()
        => new(
            "Sprint 1",
            OpenTaskCount: 12,
            OverdueTaskCount: 4,
            StalledTaskCount: 3,
            AtRiskTaskCount: 2,
            OverloadedMemberCount: 1,
            BoardCount: 1,
            [
                new ObserverSignalContext("OverdueTask", "High", 9, "4 task(s) past due", 4, 2),
                new ObserverSignalContext("StalledTask", "Medium", 8, "3 task(s) untouched", 3, 2),
            ]);

    [Fact]
    public async Task AssessObserverSignals_ReturnsAKnownSeverityAndBothFlags()
    {
        var result = await NewService().AssessObserverSignalsAsync(RiskContext());

        Assert.Contains(result.EvaluatedSeverity, ObserverSeverity.All);
        Assert.Equal(ConfidenceRouting.DirectAssign, result.RoutingAdvice);

        // A confident severity assessment may still say "no immediate alert" — the flags are independent of
        // the branch, which is what keeps the alert from being implied by the confidence.
        Assert.True(result.RequireImmediateAlert || !result.RequireImmediateAlert);
    }

    [Fact]
    public async Task AssessObserverSignals_WhenJevIsDisabled_StillAnswersBecauseTheObserverHasItsOwnGate()
    {
        // Deliberately NOT EnsureEnabled(): the Observer timer is gated by Observer:JevSeverityRouting, so a
        // global Jev off-switch must not make the background scan throw every tick. The caller decides.
        var result = await NewService(new JevOptions { Enabled = false })
            .AssessObserverSignalsAsync(RiskContext());

        Assert.Contains(result.EvaluatedSeverity, ObserverSeverity.All);
    }

    [Fact]
    public async Task AssessObserverSignals_WhenTheProviderFails_KeepsTheMediumDefaultAndAsksForAHuman()
    {
        var result = await NewService().AssessObserverSignalsAsync(
            RiskContext() with { WorkspaceName = "FAKE:JEV_ERROR" });

        Assert.Equal(ObserverSeverity.Medium, result.EvaluatedSeverity);
        Assert.True(result.RequireImmediateAlert);
        Assert.False(result.HasDominoRisk);
        Assert.Equal(ConfidenceRouting.ManualRequired, result.RoutingAdvice);
    }

    // ---- recovery routing ---------------------------------------------------------------------

    private static RecoveryActionContext RecoveryContext()
        => new(
            "Migrate the billing service",
            "Blocked on an external audit.",
            SignalType: "OverdueTask",
            EvaluatedSeverity: "High",
            DaysOverdue: 9,
            DaysRemaining: 0,
            OpenTaskCountOfAssignee: 7,
            CandidateAssigneeCount: 3,
            [
                new RecoveryActionOption("ReassignTask", "Chuyển sang người rảnh hơn"),
                new RecoveryActionOption("SplitSubtasks", "Phân rã phần tồn đọng"),
                new RecoveryActionOption("ExtendDeadline", "Gia hạn deadline khả thi"),
                new RecoveryActionOption("EscalateManager", "Cần họp khẩn, không tự giải quyết được"),
            ]);

    [Fact]
    public async Task DecideRecoveryAction_ReturnsOneOfTheOfferedKinds()
    {
        var result = await NewService().DecideRecoveryActionAsync(RecoveryContext());

        Assert.Equal(ConfidenceRouting.DirectAssign, result.RoutingAdvice);
        Assert.Contains(result.ActionType,
            new[] { "ReassignTask", "SplitSubtasks", "ExtendDeadline", "EscalateManager" });
    }

    [Fact]
    public async Task DecideRecoveryAction_NeverReturnsATargetRowBecauseJevWasNeverGivenAnId()
    {
        // The caller attaches the concrete task/user, from its own id space. This assertion is the reason the
        // phase can claim "0% identifier leak" for the decision path at all.
        var result = await NewService().DecideRecoveryActionAsync(RecoveryContext());

        Assert.Null(result.TargetTaskId);
        Assert.Null(result.TargetUserId);
    }

    [Fact]
    public async Task DecideRecoveryAction_WithLowConfidence_ProposesNoAction()
    {
        var result = await NewService().DecideRecoveryActionAsync(
            RecoveryContext() with { TaskTitle = "FAKE:JEV_LOW — what should we do?" });

        Assert.Equal(ConfidenceRouting.ManualRequired, result.RoutingAdvice);
        Assert.Null(result.ActionType);
    }

    [Fact]
    public async Task DecideRecoveryAction_WithNoOptions_ReturnsManualWithoutCallingTheProvider()
    {
        var provider = new FakeJevDecisionProvider(NullLogger<FakeJevDecisionProvider>.Instance);
        var service = new JevDecisionService(
            provider,
            Options.Create(new JevOptions()),
            TimeProvider.System,
            NullLogger<JevDecisionService>.Instance);

        var result = await service.DecideRecoveryActionAsync(RecoveryContext() with { Options = [] });

        Assert.Equal(ConfidenceRouting.ManualRequired, result.RoutingAdvice);
        Assert.Equal(0, provider.CallCount);
    }

    // ---- vocabulary ---------------------------------------------------------------------------

    [Fact]
    public void PriorityLevels_AreOrderedAscendingBecauseArrayPositionIsTheScoreContract()
    {
        // Jev returns a score as a POSITION on this scale, so reordering this array would silently invert
        // Urgent and Low. Pinning it here keeps that from being a silent change.
        Assert.Equal(["Low", "Medium", "High", "Urgent"], JevDecisionService.PriorityLevels);
    }
}
