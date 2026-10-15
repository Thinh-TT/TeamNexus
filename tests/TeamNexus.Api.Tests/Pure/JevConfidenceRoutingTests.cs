using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Jev;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Giai đoạn 14.7 §A1 — the confidence branching rules (Trụ cột 3) and the Jev answer validator.
/// <para>
/// These are the highest-value tests of the phase: they are what makes "the branch is decided by our code,
/// not by the model" a verifiable claim rather than a design intention. Everything here is a pure function,
/// so no provider, no database and no clock are involved.
/// </para>
/// </summary>
public sealed class JevConfidenceRoutingTests
{
    /// <summary>The documented defaults, clamped: 0.85 high, 0.50 medium.</summary>
    private static JevEffectiveOptions Defaults => new JevOptions().Effective;

    [Theory]
    // Above the high threshold ⇒ a single suggestion may be auto-filled.
    [InlineData(1.00, ConfidenceRouting.DirectAssign)]
    [InlineData(0.99, ConfidenceRouting.DirectAssign)]
    // THE BOUNDARY: 0.85 is inclusive on the high side (">= 0.85" in the roadmap).
    [InlineData(0.85, ConfidenceRouting.DirectAssign)]
    // Just below it ⇒ the middle band, which must NOT auto-fill.
    [InlineData(0.849999, ConfidenceRouting.OfferChoices)]
    [InlineData(0.70, ConfidenceRouting.OfferChoices)]
    // THE SECOND BOUNDARY: 0.50 is inclusive on the middle side ("0.50 <= conf < 0.85").
    [InlineData(0.50, ConfidenceRouting.OfferChoices)]
    // Below it ⇒ a human decides.
    [InlineData(0.499999, ConfidenceRouting.ManualRequired)]
    [InlineData(0.00, ConfidenceRouting.ManualRequired)]
    public void For_MapsConfidenceToTheDocumentedBranchIncludingBothBoundaries(
        double confidence, string expected)
    {
        Assert.Equal(expected, ConfidenceRouting.For(confidence, Defaults));
    }

    [Fact]
    public void For_TreatsNonFiniteConfidenceAsManualInsteadOfTrustingIt()
    {
        // NaN compares false against everything, so a naive implementation would fall through to whichever
        // branch happened to be last. Asserting it explicitly keeps that from silently changing.
        Assert.Equal(ConfidenceRouting.ManualRequired, ConfidenceRouting.For(double.NaN, Defaults));
        Assert.Equal(ConfidenceRouting.ManualRequired, ConfidenceRouting.For(double.PositiveInfinity, Defaults));
        Assert.Equal(ConfidenceRouting.ManualRequired, ConfidenceRouting.For(double.NegativeInfinity, Defaults));
    }

    [Fact]
    public void For_ClampsAnInvertedThresholdPairSoTheMiddleBandCannotDisappear()
    {
        // A misconfigured pair (high 0.2, medium 0.9) would otherwise leave NO confidence value able to
        // reach OfferChoices — the top-2/3 UI would be unreachable with no error anywhere.
        var options = new JevOptions
        {
            HighConfidenceThreshold = 0.20,
            MediumConfidenceThreshold = 0.90,
        }.Effective;

        Assert.True(options.MediumConfidenceThreshold >= 0.05);
        Assert.True(options.HighConfidenceThreshold > options.MediumConfidenceThreshold);

        // The middle band must still be reachable.
        var probe = (options.MediumConfidenceThreshold + options.HighConfidenceThreshold) / 2;
        Assert.Equal(ConfidenceRouting.OfferChoices, ConfidenceRouting.For(probe, options));
    }

    [Fact]
    public void For_ClampsOutOfRangeThresholdsIntoAUsableWindow()
    {
        var options = new JevOptions
        {
            HighConfidenceThreshold = 5.0,   // nonsense high
            MediumConfidenceThreshold = -3.0, // nonsense low
        }.Effective;

        Assert.InRange(options.MediumConfidenceThreshold, 0.05, 0.90);
        Assert.InRange(options.HighConfidenceThreshold, options.MediumConfidenceThreshold + 0.05, 0.99);
    }

    [Theory]
    [InlineData(ConfidenceRouting.DirectAssign, true)]
    [InlineData(ConfidenceRouting.OfferChoices, false)]
    [InlineData(ConfidenceRouting.ManualRequired, false)]
    [InlineData(ConfidenceRouting.AwaitingClarification, false)]
    [InlineData(null, false)]
    public void AllowsAutoFill_IsTrueOnlyForTheSingleSuggestionBranch(string? advice, bool expected)
    {
        Assert.Equal(expected, ConfidenceRouting.AllowsAutoFill(advice));
    }

    [Fact]
    public void MaxOfferedCandidates_IsThreeSoTheMiddleBandStaysANudgeNotAMenu()
    {
        Assert.Equal(3, ConfidenceRouting.MaxOfferedCandidates);
    }
}

/// <summary>
/// Giai đoạn 14.7 §A1 — the anti-hallucination boundary for the System One path.
/// A rejected answer must never throw: it degrades to "no suggestion".
/// </summary>
public sealed class JevDecisionValidatorTests
{
    private static JevQuestion ChoiceQuestion(params string[] labels)
        => new(
            "q",
            JevQuestionType.Choice,
            "instructions",
            ChoiceCriteria: labels.ToDictionary(label => label, label => label, StringComparer.Ordinal));

    private static JevAnswer ChoiceAnswer(string choice, Dictionary<string, double> probabilities, double? confidence = 0.9)
        => new(JevQuestionType.Choice, null, choice, null, probabilities, confidence,
            new Dictionary<string, string>(StringComparer.Ordinal));

    // ---- choice -------------------------------------------------------------------------------

    [Fact]
    public void Choice_IsUsableWhenItNamesAnOfferedLabelAndTheDistributionSumsToOne()
    {
        var question = ChoiceQuestion("Backend", "Frontend");

        Assert.True(JevDecisionValidator.IsUsable(
            ChoiceAnswer("Backend", new() { ["Backend"] = 0.84, ["Frontend"] = 0.16 }),
            question));
    }

    [Fact]
    public void Choice_IsUsableRegardlessOfLabelCasingBecauseTheProviderEchoesOurOwnHandle()
    {
        var question = ChoiceQuestion("Backend", "Frontend");

        // The label came from us, so a casing difference must resolve rather than silently become
        // "no suggestion" — a real failure mode with providers that normalise case.
        Assert.True(JevDecisionValidator.IsUsable(
            ChoiceAnswer("backend", new() { ["BACKEND"] = 0.9, ["frontend"] = 0.1 }),
            question));
    }

    [Fact]
    public void Choice_IsRejectedWhenItInventsALabelWeNeverOffered()
    {
        var question = ChoiceQuestion("Backend", "Frontend");

        Assert.False(JevDecisionValidator.IsUsable(
            ChoiceAnswer("Mobile", new() { ["Backend"] = 0.84, ["Frontend"] = 0.16 }),
            question));
    }

    [Fact]
    public void Choice_IsRejectedWhenAProbabilityKeyIsALabelWeNeverOffered()
    {
        // The winner is valid but a stray key would reach the UI as an invented option.
        var question = ChoiceQuestion("Backend", "Frontend");

        Assert.False(JevDecisionValidator.IsUsable(
            ChoiceAnswer("Backend", new() { ["Backend"] = 0.8, ["Mobile"] = 0.2 }),
            question));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    public void Choice_IsRejectedWhenAnyProbabilityFallsOutsideTheUnitInterval(double bad)
    {
        var question = ChoiceQuestion("Backend", "Frontend");

        Assert.False(JevDecisionValidator.IsUsable(
            ChoiceAnswer("Backend", new() { ["Backend"] = bad, ["Frontend"] = 0.1 }),
            question));
    }

    [Fact]
    public void Choice_IsRejectedWhenTheDistributionDoesNotAddUp()
    {
        // A distribution summing to 0.3 cannot be branched on: the percentages shown next to each option
        // would be a lie. The tolerance is ±0.1, so 0.3 is well outside it.
        var question = ChoiceQuestion("Backend", "Frontend");

        Assert.False(JevDecisionValidator.IsUsable(
            ChoiceAnswer("Backend", new() { ["Backend"] = 0.2, ["Frontend"] = 0.1 }),
            question));
    }

    [Fact]
    public void Choice_AcceptsAIsWithinTheSumToleranceButRejectsMore()
    {
        var question = ChoiceQuestion("Backend", "Frontend");

        // 0.96 total: rounded percentages from the provider legitimately drift this far.
        Assert.True(JevDecisionValidator.IsUsable(
            ChoiceAnswer("Backend", new() { ["Backend"] = 0.9, ["Frontend"] = 0.06 }),
            question));

        // 0.7 total: not drift, a broken payload.
        Assert.False(JevDecisionValidator.IsUsable(
            ChoiceAnswer("Backend", new() { ["Backend"] = 0.6, ["Frontend"] = 0.1 }),
            question));
    }

    [Fact]
    public void Choice_IsRejectedWhenTheDistributionIsEmpty()
    {
        // Without a distribution there is nothing to render next to the winner; inventing 1.0 for it would
        // turn "we were given nothing" into "we are certain".
        var question = ChoiceQuestion("Backend", "Frontend");

        Assert.False(JevDecisionValidator.IsUsable(
            ChoiceAnswer("Backend", new Dictionary<string, double>()),
            question));
    }

    [Fact]
    public void Choice_IsRejectedWhenTheQuestionOffersNoLabelsAtAll()
    {
        // A question with no options cannot be answered truthfully, so any answer must be discarded.
        Assert.False(JevDecisionValidator.IsUsable(
            ChoiceAnswer("Backend", new() { ["Backend"] = 1.0 }),
            new JevQuestion("q", JevQuestionType.Choice, "instructions")));
    }

    // ---- identifier leak (defence in depth) ---------------------------------------------------

    [Fact]
    public void Choice_IsRejectedWhenALabelCarriesAGuidEvenThoughJevCannotProduceOne()
    {
        // Jev is never sent an id, so this cannot happen today. It is asserted anyway: the whole phase
        // claims "0% identifier leak", and Phase 14.6 needed three layers to achieve that for the LLM path.
        var guid = Guid.NewGuid().ToString();
        var question = ChoiceQuestion(guid, "Frontend");

        Assert.False(JevDecisionValidator.IsUsable(
            ChoiceAnswer(guid, new() { [guid] = 1.0 }),
            question));
    }

    [Fact]
    public void Score_IsRejectedWhenALegendValueCarriesAGuid()
    {
        var guid = Guid.NewGuid().ToString();
        var question = new JevQuestion("q", JevQuestionType.Score, "instructions", ScoreCriteria: ["Low", "High"]);
        var answer = new JevAnswer(
            JevQuestionType.Score, null, null, 1,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["1"] = 1.0 },
            0.9,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["1"] = guid });

        Assert.False(JevDecisionValidator.IsUsable(answer, question));
    }

    // ---- score --------------------------------------------------------------------------------

    [Fact]
    public void Score_IsUsableOnlyInsideTheScaleTheServerProvided()
    {
        var question = new JevQuestion("q", JevQuestionType.Score, "instructions", ScoreCriteria: ["Low", "Medium", "High"]);

        JevAnswer At(double score) => new(
            JevQuestionType.Score, null, null, score,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["0"] = 1.0 },
            0.9, new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.True(JevDecisionValidator.IsUsable(At(0), question));
        Assert.True(JevDecisionValidator.IsUsable(At(2), question));

        // Position 3 does not exist on a 3-level scale, and -1 does not either.
        Assert.False(JevDecisionValidator.IsUsable(At(3), question));
        Assert.False(JevDecisionValidator.IsUsable(At(-1), question));
        Assert.False(JevDecisionValidator.IsUsable(At(double.NaN), question));
        Assert.False(JevDecisionValidator.IsUsable(At(double.PositiveInfinity), question));
    }

    [Fact]
    public void Score_IsRejectedWhenAProbabilityKeyIsOutsideTheScale()
    {
        var question = new JevQuestion("q", JevQuestionType.Score, "instructions", ScoreCriteria: ["Low", "High"]);
        var answer = new JevAnswer(
            JevQuestionType.Score, null, null, 1,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["1"] = 0.6, ["7"] = 0.4 },
            0.9, new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.False(JevDecisionValidator.IsUsable(answer, question));
    }

    // ---- noul ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0, true)]
    [InlineData(0.5, true)]
    [InlineData(1.0, true)]
    [InlineData(-0.01, false)]
    [InlineData(1.01, false)]
    public void Noul_IsUsableOnlyWithinTheUnitInterval(double probability, bool expected)
    {
        var question = new JevQuestion("q", JevQuestionType.Noul, "instructions");
        var answer = new JevAnswer(
            JevQuestionType.Noul, probability, null, null,
            new Dictionary<string, double>(StringComparer.Ordinal), null,
            new Dictionary<string, string>(StringComparer.Ordinal));

        Assert.Equal(expected, JevDecisionValidator.IsUsable(answer, question));
    }

    // ---- type mismatch / nulls ----------------------------------------------------------------

    [Fact]
    public void IsUsable_RejectsATypeMismatchBetweenQuestionAndAnswer()
    {
        // A `choice` payload answered a `noul` question: the shape cannot be trusted, so it is discarded
        // rather than coerced.
        var question = new JevQuestion("q", JevQuestionType.Noul, "instructions");
        var answer = ChoiceAnswer("Backend", new() { ["Backend"] = 1.0 });

        Assert.False(JevDecisionValidator.IsUsable(answer, question));
    }

    [Fact]
    public void IsUsable_RejectsNullsInsteadOfThrowing()
    {
        // Callers reach this with whatever the provider produced, so nulls must be a "reject", never a 500.
        Assert.False(JevDecisionValidator.IsUsable(null, ChoiceQuestion("Backend")));
        Assert.False(JevDecisionValidator.IsUsable(ChoiceAnswer("Backend", new() { ["Backend"] = 1.0 }), null!));
    }

    // ---- answer normalization -----------------------------------------------------------------

    [Fact]
    public void NormalizeAnswers_IsCaseInsensitiveAndDropsNullAnswers()
    {
        var response = new JevDecisionResponse(
            new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
            {
                ["Domain"] = ChoiceAnswer("Backend", new() { ["Backend"] = 1.0 }),
                ["gone"] = null!,
            },
            "m", "p", new JevUsage(null, null, null));

        var normalized = JevDecisionValidator.NormalizeAnswers(response);

        Assert.True(normalized.ContainsKey("domain"));
        Assert.Single(normalized);
    }

    [Fact]
    public void NormalizeAnswers_HandlesANullResponse()
    {
        Assert.Empty(JevDecisionValidator.NormalizeAnswers(null));
    }
}

/// <summary>
/// Giai đoạn 14.7 §A1 — <c>JevOptions</c>: the two independent URL knobs and the defensive clamping.
/// </summary>
public sealed class JevOptionsTests
{
    [Fact]
    public void DecisionsUri_ComposesHostAndPathFromTheTwoIndependentKnobs()
    {
        var options = new JevOptions();

        Assert.Equal(new Uri("https://openrouter.ai/api/alpha/decisions"), options.DecisionsUri);
    }

    [Theory]
    // Trailing slash on either side must not produce a double slash or a dropped segment.
    [InlineData("https://openrouter.ai/", "/api/alpha/decisions")]
    [InlineData("https://openrouter.ai", "api/alpha/decisions")]
    [InlineData("https://openrouter.ai/", "api/alpha/decisions")]
    public void DecisionsUri_NormalisesSlashesSoTheAlphaSurfaceCanBeRepointedByConfiguration(
        string baseUrl, string path)
    {
        var options = new JevOptions { BaseUrl = baseUrl, DecisionsPath = path };

        Assert.Equal(new Uri("https://openrouter.ai/api/alpha/decisions"), options.DecisionsUri);
    }

    [Fact]
    public void DecisionsUri_SupportsSwitchingToTheSystemOneSurfaceWithoutACodeChange()
    {
        var options = new JevOptions { DecisionsPath = "/api/v1/systemone" };

        Assert.Equal(new Uri("https://openrouter.ai/api/v1/systemone"), options.DecisionsUri);
    }

    [Fact]
    public void DecisionsUri_FallsBackToTheDocumentedDefaultsWhenBothKnobsAreBlank()
    {
        var options = new JevOptions { BaseUrl = "  ", DecisionsPath = "  " };

        Assert.Equal(new Uri("https://openrouter.ai/api/alpha/decisions"), options.DecisionsUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void HasApiKey_IsFalseForBlankOrWhitespaceSoTheFakeProviderIsSelected(string key)
    {
        Assert.False(new JevOptions { ApiKey = key }.HasApiKey);
    }

    [Fact]
    public void HasApiKey_IsTrueForArealLookingKey()
    {
        Assert.True(new JevOptions { ApiKey = "sk-or-v1-test" }.HasApiKey);
    }

    [Fact]
    public void EffectiveModel_FallsBackToTheDocumentedModelWhenBlank()
    {
        Assert.Equal("typesafe/jev-1.13", new JevOptions { Model = "  " }.EffectiveModel);
    }

    [Fact]
    public void Effective_ClampsEveryKnobAwayFromValuesThatWouldDisableAGuardrail()
    {
        var options = new JevOptions
        {
            TimeoutSeconds = 0,
            MaxStateChars = 0,
            MaxCandidates = 0,
            MaxQuestionsPerRequest = 0,
            RagChunkChars = 0,
            RagMaxChunksPerDocument = 0,
            RagMaxContextChars = 0,
            RagEmbeddingDimension = 0,
        }.Effective;

        Assert.True(options.TimeoutSeconds >= 1);
        Assert.True(options.MaxStateChars >= 200);
        Assert.True(options.MaxCandidates >= 1);
        Assert.True(options.MaxQuestionsPerRequest >= 1);
        Assert.True(options.RagChunkChars >= 200);
        Assert.True(options.RagMaxChunksPerDocument >= 1);
        Assert.True(options.RagMaxContextChars >= 200);
        Assert.True(options.RagEmbeddingDimension >= 64);
    }

    [Fact]
    public void Timeout_IsDerivedFromTheClampedValueNotTheRawOne()
    {
        Assert.Equal(TimeSpan.FromSeconds(1), new JevOptions { TimeoutSeconds = 0 }.Timeout);
    }
}

/// <summary>
/// Giai đoạn 14.7 §A1 — the offline provider. It must be deterministic and must never produce a label the
/// caller did not offer, because the whole offline verification story depends on it agreeing with the
/// validator.
/// </summary>
public sealed class FakeJevDecisionProviderTests
{
    private static FakeJevDecisionProvider NewProvider() => new(NullLogger<FakeJevDecisionProvider>.Instance);

    private static JevDecisionRequest ChoiceRequest(params string[] labels)
        => new(
            [new JevQuestion(
                "domain",
                JevQuestionType.Choice,
                "Which domain?",
                ChoiceCriteria: labels.ToDictionary(label => label, label => label, StringComparer.Ordinal))],
            """{"title":"Fix the login bug","description":"Users cannot sign in."}""");

    [Fact]
    public async Task DecideAsync_AlwaysReUsesTheLabelsTheCallerOffered()
    {
        // The FakeAiProvider precedent: build the sample out of what the server sent, so the response always
        // survives server-side validation instead of depending on a fixture that can drift.
        var provider = NewProvider();

        var response = await provider.DecideAsync(ChoiceRequest("Backend", "Frontend", "DevOps"));

        var answer = response.Answers["domain"];
        Assert.Contains(answer.Choice, new[] { "Backend", "Frontend", "DevOps" });
        Assert.All(answer.Probabilities.Keys, key => Assert.Contains(key, new[] { "Backend", "Frontend", "DevOps" }));
    }

    [Fact]
    public async Task DecideAsync_DefaultScenarioIsConfidentEnoughToAutoFill()
    {
        var provider = NewProvider();

        var response = await provider.DecideAsync(ChoiceRequest("Backend", "Frontend"));
        var options = new JevOptions().Effective;

        Assert.True(response.Answers["domain"].Confidence >= options.HighConfidenceThreshold);
    }

    [Fact]
    public async Task DecideAsync_MediumSentinelLandsInTheMiddleBand()
    {
        var provider = NewProvider();
        var request = ChoiceRequest("Backend", "Frontend") with
        {
            StateJson = """{"title":"FAKE:JEV_MED — triage this"}""",
        };

        var response = await provider.DecideAsync(request);
        var options = new JevOptions().Effective;
        var confidence = response.Answers["domain"].Confidence!.Value;

        Assert.InRange(confidence, options.MediumConfidenceThreshold, options.HighConfidenceThreshold - 0.000001);
    }

    [Fact]
    public async Task DecideAsync_LowSentinelLandsBelowTheFloor()
    {
        var provider = NewProvider();
        var request = ChoiceRequest("Backend", "Frontend") with
        {
            StateJson = """{"title":"FAKE:JEV_LOW — triage this"}""",
        };

        var response = await provider.DecideAsync(request);
        var options = new JevOptions().Effective;

        Assert.True(response.Answers["domain"].Confidence < options.MediumConfidenceThreshold);
    }

    [Fact]
    public async Task DecideAsync_ErrorSentinelThrowsSoTheDegradePathIsReachableOffline()
    {
        var provider = NewProvider();
        var request = ChoiceRequest("Backend") with
        {
            StateJson = """{"title":"FAKE:JEV_ERROR"}""",
        };

        await Assert.ThrowsAsync<JevDecisionException>(() => provider.DecideAsync(request));
    }

    [Fact]
    public async Task DecideAsync_IsDeterministicAcrossRuns()
    {
        // Two identical calls must produce byte-identical answers: a suite that compares a stored snapshot
        // would otherwise fail intermittently for no reason the harness chose.
        var provider = NewProvider();

        var first = await provider.DecideAsync(ChoiceRequest("Backend", "Frontend"));
        var second = await provider.DecideAsync(ChoiceRequest("Backend", "Frontend"));

        Assert.Equal(first.Answers["domain"].Choice, second.Answers["domain"].Choice);
        Assert.Equal(first.Answers["domain"].Confidence, second.Answers["domain"].Confidence);
        Assert.Equal(
            first.Answers["domain"].Probabilities.OrderBy(pair => pair.Key).ToList(),
            second.Answers["domain"].Probabilities.OrderBy(pair => pair.Key).ToList());
    }

    [Fact]
    public async Task DecideAsync_CountsItsCallsSoASuiteCanProveAGuardShortCircuited()
    {
        var provider = NewProvider();

        Assert.Equal(0, provider.CallCount);

        await provider.DecideAsync(ChoiceRequest("Backend"));
        await provider.DecideAsync(ChoiceRequest("Backend"));

        Assert.Equal(2, provider.CallCount);
    }

    [Fact]
    public async Task DecideAsync_SurvivesAMalformedStateInsteadOfFailingTheSuite()
    {
        // A fake must never be the reason a test fails: a state that is not JSON simply yields the default
        // (confident) scenario.
        var provider = NewProvider();
        var request = ChoiceRequest("Backend") with { StateJson = "not json at all" };

        var response = await provider.DecideAsync(request);

        Assert.True(response.Answers.ContainsKey("domain"));
    }

    [Fact]
    public async Task DecideAsync_AnswersEveryQuestionItWasAskedWhateverThePrimitive()
    {
        var provider = NewProvider();
        var request = new JevDecisionRequest(
            [
                new JevQuestion("domain", JevQuestionType.Choice, "which?", ChoiceCriteria: new Dictionary<string, string> { ["Backend"] = "b" }),
                new JevQuestion("priority", JevQuestionType.Score, "how urgent?", ScoreCriteria: ["Low", "Medium", "High", "Urgent"]),
                new JevQuestion("security", JevQuestionType.Noul, "security review?"),
            ],
            """{"title":"anything"}""");

        var response = await provider.DecideAsync(request);

        Assert.Equal(3, response.Answers.Count);
        Assert.Equal(JevQuestionType.Choice, response.Answers["domain"].Type);
        Assert.Equal(JevQuestionType.Score, response.Answers["priority"].Type);
        Assert.Equal(JevQuestionType.Noul, response.Answers["security"].Type);

        // The score must land inside the scale the caller provided.
        Assert.InRange(response.Answers["priority"].Score!.Value, 0, 3);
    }

    [Fact]
    public async Task DecideAsync_ReportsUsageSoCostAndTokenPlumbingIsExercisedOffline()
    {
        // Deliberately not zero: a suite asserting "usage was recorded" must be able to pass offline.
        var provider = NewProvider();

        var response = await provider.DecideAsync(ChoiceRequest("Backend"));

        Assert.NotNull(response.Usage);
        Assert.NotNull(response.Usage.InputTokens);
        Assert.NotNull(response.Usage.OutputTokens);
    }

    [Fact]
    public async Task DecideAsync_WithAChoiceQuestionThatHasNoLabels_EmitsATypedButEmptyAnswer()
    {
        // Inventing a label the caller never offered would produce output the real validator rejects — i.e.
        // a fake that hides the very bug it exists to expose.
        var provider = NewProvider();
        var request = new JevDecisionRequest(
            [new JevQuestion("domain", JevQuestionType.Choice, "which?")],
            """{"title":"anything"}""");

        var response = await provider.DecideAsync(request);

        Assert.Null(response.Answers["domain"].Choice);
        Assert.Empty(response.Answers["domain"].Probabilities);
    }
}
