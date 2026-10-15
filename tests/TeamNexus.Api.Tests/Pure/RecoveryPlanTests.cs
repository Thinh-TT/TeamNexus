using TeamNexus.Modules.Ai.DTOs;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Appliers;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Giai đoạn 14.7 §A5 Phần A — the directive that hands Jev's severity verdict down to the LLM.
/// <para>
/// The whole point of the Hybrid split is that the decision is made by our code and only explained by the
/// model, so this text IS the contract between the two halves. Pure, so it needs no provider.
/// </para>
/// </summary>
public sealed class ObserverJevDirectiveTests
{
    [Theory]
    [InlineData("Low")]
    [InlineData("Medium")]
    [InlineData("High")]
    [InlineData("Critical")]
    public void Directive_NamesTheSeverityItWasGiven(string severity)
    {
        var directive = ObserverPrompts.BuildJevSeverityDirective(severity, false, false);

        Assert.Contains(severity, directive, StringComparison.Ordinal);

        // A directive with no severity would leave the model free to re-decide, which defeats the point.
        Assert.Contains("Mức nghiêm trọng tổng thể", directive, StringComparison.Ordinal);
    }

    [Fact]
    public void Directive_CanonicalisesCasingSoTheModelSeesOneSpelling()
    {
        var directive = ObserverPrompts.BuildJevSeverityDirective("  cRiTiCaL  ", false, false);

        Assert.Contains("Critical", directive, StringComparison.Ordinal);
        Assert.DoesNotContain("cRiTiCaL", directive, StringComparison.Ordinal);
    }

    [Fact]
    public void Directive_AddsTheAlertLineOnlyWhenAnImmediateAlertWasRequested()
    {
        var quiet = ObserverPrompts.BuildJevSeverityDirective("High", false, false);
        var loud = ObserverPrompts.BuildJevSeverityDirective("High", true, false);

        Assert.DoesNotContain("cảnh báo khẩn", quiet, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cảnh báo khẩn", loud, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Directive_AddsTheDominoLineOnlyWhenCascadeRiskWasDetected()
    {
        var flat = ObserverPrompts.BuildJevSeverityDirective("High", false, false);
        var domino = ObserverPrompts.BuildJevSeverityDirective("High", false, true);

        Assert.DoesNotContain("dây chuyền", flat, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dây chuyền", domino, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Emergency")]
    [InlineData("0")]
    public void Directive_IsEmptyForAnUnusableSeveritySoTheModelFallsBackToItsOwnReading(string? severity)
    {
        // An unknown severity must yield NOTHING rather than a half directive: a prompt that says
        // "the severity is: " with no value is worse than no instruction at all.
        Assert.Equal(string.Empty, ObserverPrompts.BuildJevSeverityDirective(severity, false, false));
    }

    [Fact]
    public void Directive_StartsWithANewLineSoItDoesNotRunIntoTheSystemPromptRules()
    {
        // The directive is appended to the system prompt template, whose last line is a rule. Without a leading
        // newline the first directive line would be glued onto it.
        var directive = ObserverPrompts.BuildJevSeverityDirective("High", false, false);

        Assert.StartsWith("\n", directive);
    }

    [Fact]
    public void BuildRequest_PutsTheDirectiveInTheSystemPromptAndLeavesTheUserPromptAsPureJson()
    {
        // ⚠️ The regression this test exists for: the directive was originally appended to the USER prompt,
        // which broke the offline provider's JsonDocument.Parse — it silently returned zero findings and the
        // Observer stopped alerting. The user prompt must stay `marker + JSON payload` and nothing else.
        var snapshot = new ObserverWorkspaceSnapshot(
            Guid.NewGuid(),
            [
                new ObserverTaskSnapshot(
                    Guid.NewGuid(), Guid.NewGuid(), "Board", Guid.NewGuid(), "Todo", "Thẻ quá hạn",
                    null, null, DateTimeOffset.UtcNow.AddDays(-30),
                    DateTimeOffset.UtcNow.AddDays(-60), DateTimeOffset.UtcNow.AddDays(-1), false, 0, null),
            ],
            [new ObserverColumnSnapshot(Guid.NewGuid(), Guid.NewGuid(), "Todo", false)],
            DateTimeOffset.UtcNow);

        var signals = ObserverSignalDetector.Analyze(snapshot, new ObserverOptions().ToThresholds());
        Assert.NotEmpty(signals.Signals);

        var directive = ObserverPrompts.BuildJevSeverityDirective("Critical", true, true);
        var request = ObserverSummarizer.BuildRequest(snapshot, signals, new ObserverOptions(), directive);

        // The directive is in the system prompt…
        Assert.Contains("ĐÁNH GIÁ MỨC ĐỘ ĐÃ CHỐT", request.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Critical", request.SystemPrompt, StringComparison.Ordinal);

        // …and the user prompt is still exactly marker + parseable JSON.
        Assert.DoesNotContain("ĐÁNH GIÁ MỨC ĐỘ ĐÃ CHỐT", request.UserPrompt, StringComparison.Ordinal);

        var firstNewline = request.UserPrompt.IndexOf('\n');
        Assert.Equal(ObserverPrompts.AgentMarker, request.UserPrompt[..firstNewline]);

        var json = request.UserPrompt[(firstNewline + 1)..].Trim();

        // This is the exact parse that used to throw.
        using var document = System.Text.Json.JsonDocument.Parse(json);
        Assert.True(document.RootElement.TryGetProperty("signals", out _));
    }
}

/// <summary>
/// Giai đoạn 14.7 §A5 Phần B — the recovery-plan vocabulary and the stored payload shape.
/// </summary>
public sealed class RecoveryPlanTests
{
    [Fact]
    public void Kinds_ContainTheFourDocumentedRecoveryActions()
    {
        // These strings are the `Choice` option keys handed to Jev AND the values RecoveryPlanApplier switches
        // on, so the list is a contract in two directions.
        Assert.Equal(
            ["ReassignTask", "SplitSubtasks", "ExtendDeadline", "EscalateManager"],
            RecoveryPlanKinds.All);
    }

    [Theory]
    [InlineData("ReassignTask", true)]
    [InlineData("reassesstask", false)]
    [InlineData(" reassigntask ", true)]
    [InlineData("splitsubtasks", true)]
    [InlineData("escalatemanager", true)]
    [InlineData("DeleteEverything", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsKnown_MatchesCaseInsensitivelyAndRejectsUnknownKinds(string? kind, bool expected)
    {
        Assert.Equal(expected, RecoveryPlanKinds.IsKnown(kind));
    }

    [Fact]
    public void Canonical_ReturnsTheDocumentedSpellingOrNull()
    {
        Assert.Equal("EscalateManager", RecoveryPlanKinds.Canonical("escalatemanager"));
        Assert.Null(RecoveryPlanKinds.Canonical("Nope"));
        Assert.Null(RecoveryPlanKinds.Canonical(null));
    }

    [Fact]
    public void RecoveryPlan_IsASingleActionTypeWithTheKindInsideThePayload()
    {
        // Deliberately ONE type: four types would fragment the AI history list and the ?action= filter for no
        // gain. If someone splits them, this assertion is where the discussion starts.
        Assert.Equal("RecoveryPlan", AiActionTypes.RecoveryPlan);
        Assert.DoesNotContain(AiActionTypes.RecoveryPlan, RecoveryPlanKinds.All);
    }

    [Fact]
    public void EscalateManager_IsNotAnActionTypeBecauseItWritesNothing()
    {
        // Escalation is a KIND, not an action: there is no applier for it and nothing to approve beyond
        // "someone should look at this".
        Assert.DoesNotContain(RecoveryPlanKinds.EscalateManager, typeof(AiActionTypes)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Select(field => field.GetValue(null) as string));
    }

    [Fact]
    public void MaxExtensionDays_IsBoundedSoAPlanCannotPushADeadlineYearsOut()
    {
        Assert.InRange(RecoveryPlanApplier.MaxExtensionDays, 1, 365);
    }

    [Fact]
    public void Payload_RoundTripsThroughJsonKeepingEveryKindSpecificField()
    {
        var payload = new RecoveryPlanPayload(
            RecoveryPlanKinds.SplitSubtasks,
            "summary text",
            TaskId: Guid.NewGuid(),
            AssigneeId: Guid.NewGuid(),
            ExtensionDays: 14,
            BoardId: Guid.NewGuid(),
            Tasks:
            [
                new SmartSetupTaskProposal("Sub one", "desc", "High", [], null),
            ]);

        var json = System.Text.Json.JsonSerializer.Serialize(
            payload, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        var parsed = System.Text.Json.JsonSerializer.Deserialize<RecoveryPlanPayload>(
            json, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(parsed);
        Assert.Equal(payload.Kind, parsed!.Kind);
        Assert.Equal(payload.TaskId, parsed.TaskId);
        Assert.Equal(payload.BoardId, parsed.BoardId);
        Assert.Equal(payload.ExtensionDays, parsed.ExtensionDays);
        Assert.Single(parsed.Tasks!);
    }

    [Fact]
    public void ParsePlan_RejectsAMissingOrMalformedPayloadWithA400NotA500()
    {
        // The payload is written by our own code, so an unreadable one is a client-visible contract violation
        // (a hand-edited row), not a server fault.
        Assert.Throws<TeamNexus.Modules.Board.Services.BadRequestException>(
            () => RecoveryPlanApplier.ParsePlan(new AiActionLog { AfterSnapshot = null }));

        Assert.Throws<TeamNexus.Modules.Board.Services.BadRequestException>(
            () => RecoveryPlanApplier.ParsePlan(new AiActionLog { AfterSnapshot = "{not json" }));
    }

    [Fact]
    public void ParsePlan_ReadsAWellFormedPayload()
    {
        var log = new AiActionLog
        {
            AfterSnapshot = $$"""{"kind":"ExtendDeadline","summary":"s","extensionDays":14}""",
        };

        var parsed = RecoveryPlanApplier.ParsePlan(log);

        Assert.Equal("ExtendDeadline", parsed.Kind);
        Assert.Equal(14, parsed.ExtensionDays);
    }
}

/// <summary>
/// Giai đoạn 14.7 §A5 — the new snapshot readers on <c>AiActionService</c>.
/// </summary>
public sealed class AiActionSnapshotReaderTests
{
    [Fact]
    public void ReadBool_IsTrueOnlyForAJsonTrueAndFalseForEverythingElse()
    {
        const string snapshot = """{"yes":true,"no":false,"text":"true","nothing":null}""";

        Assert.True(AiActionService.ReadBool(snapshot, "yes"));
        Assert.False(AiActionService.ReadBool(snapshot, "no"));

        // A string "true" is NOT a boolean: coercing it would make a malformed payload look like a decision.
        Assert.False(AiActionService.ReadBool(snapshot, "text"));
        Assert.False(AiActionService.ReadBool(snapshot, "nothing"));
        Assert.False(AiActionService.ReadBool(snapshot, "absent"));
        Assert.False(AiActionService.ReadBool(null, "yes"));
    }

    [Fact]
    public void ReadDateTimeOffset_ParsesAnIsoTimestampAndIsNullForAnythingElse()
    {
        var stamp = new DateTimeOffset(2026, 10, 14, 12, 30, 0, TimeSpan.Zero);
        var snapshot = $$"""{"when":"{{stamp:O}}","text":"not a date","absent":null}""";

        Assert.Equal(stamp, AiActionService.ReadDateTimeOffset(snapshot, "when"));
        Assert.Null(AiActionService.ReadDateTimeOffset(snapshot, "text"));
        Assert.Null(AiActionService.ReadDateTimeOffset(snapshot, "absent"));
        Assert.Null(AiActionService.ReadDateTimeOffset(null, "when"));
    }

    [Fact]
    public void ReadString_IsCaseInsensitiveOnThePropertyName()
    {
        Assert.Equal("k", AiActionService.ReadString("""{"RecoveryKind":"k"}""", "recoveryKind"));
    }
}
