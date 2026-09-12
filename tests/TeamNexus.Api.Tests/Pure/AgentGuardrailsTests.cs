using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 8 §2.3 — the four Phase 7 guardrails, tested purely (no DB, no provider).
/// <para>
/// Priority 1: <c>AgentGuardrails</c> is <c>public static</c> precisely so this suite can exist
/// (Phase 7 D19). Every threshold is asserted <b>at the boundary</b> and one step beyond, and the
/// documented precedence (ToolLimit → TokenBudget → TimeLimit) is pinned so a future reorder
/// cannot silently change the Manager-facing stop reason.
/// </para>
/// </summary>
public sealed class AgentGuardrailsTests
{
    /// <summary>Production thresholds from <c>appsettings.json</c> (Agent section).</summary>
    private static AgentEffectiveOptions Defaults => new AgentOptions().Effective;

    // ---- no stop ------------------------------------------------------------

    [Fact]
    public void Evaluate_WhenNothingIsExceeded_KeepsLooping()
    {
        var state = new AgentGuardrailState(0, 0, 0, 0, TimeSpan.Zero);

        Assert.Null(AgentGuardrails.Evaluate(state, Defaults));
    }

    [Fact]
    public void Evaluate_AtEveryExactThreshold_KeepsLooping()
    {
        var options = Defaults;

        var state = new AgentGuardrailState(
            ToolCalls: options.MaxToolCalls,
            LlmCalls: options.MaxRunLlmCalls,
            PromptTokens: options.MaxRunTokens,
            CompletionTokens: 0,
            Elapsed: TimeSpan.FromSeconds(options.RunTimeoutSeconds));

        // The contract is strictly "> threshold", mirroring Phase 5's "idle longer than StalledDays".
        Assert.Null(AgentGuardrails.Evaluate(state, options));
    }

    // ---- ToolLimit ----------------------------------------------------------

    [Fact]
    public void Evaluate_OneToolCallPastTheCap_StopsWithToolLimit()
    {
        var options = Defaults;
        var state = new AgentGuardrailState(options.MaxToolCalls + 1, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(AgentStopReason.ToolLimit, AgentGuardrails.Evaluate(state, options));
    }

    // ---- TokenBudget --------------------------------------------------------

    [Fact]
    public void Evaluate_OneTokenPastTheBudget_StopsWithTokenBudget()
    {
        var options = Defaults;
        var state = new AgentGuardrailState(0, 0, options.MaxRunTokens + 1, 0, TimeSpan.Zero);

        Assert.Equal(AgentStopReason.TokenBudget, AgentGuardrails.Evaluate(state, options));
    }

    [Fact]
    public void Evaluate_CountsPromptAndCompletionTokensTogether()
    {
        var options = Defaults;

        // Neither half exceeds the budget alone, but the sum does — the budget is on TotalTokens.
        var half = (options.MaxRunTokens / 2) + 1;
        var state = new AgentGuardrailState(0, 0, half, half, TimeSpan.Zero);

        Assert.Equal(options.MaxRunTokens + 2, state.TotalTokens);
        Assert.Equal(AgentStopReason.TokenBudget, AgentGuardrails.Evaluate(state, options));
    }

    // ---- TimeLimit ----------------------------------------------------------

    [Fact]
    public void Evaluate_OneSecondPastTheTimeout_StopsWithTimeLimit()
    {
        var options = Defaults;
        var state = new AgentGuardrailState(
            0, 0, 0, 0, TimeSpan.FromSeconds(options.RunTimeoutSeconds + 1));

        Assert.Equal(AgentStopReason.TimeLimit, AgentGuardrails.Evaluate(state, options));
    }

    // ---- precedence ---------------------------------------------------------

    [Fact]
    public void Evaluate_WhenSeveralBudgetsAreBlown_ReportsToolLimitFirst()
    {
        var options = Defaults;

        var state = new AgentGuardrailState(
            ToolCalls: options.MaxToolCalls + 5,
            LlmCalls: options.MaxRunLlmCalls + 5,
            PromptTokens: options.MaxRunTokens + 5,
            CompletionTokens: 0,
            Elapsed: TimeSpan.FromSeconds(options.RunTimeoutSeconds + 5));

        // Documented precedence: the caller must be told about the cheapest budget to act on first.
        Assert.Equal(AgentStopReason.ToolLimit, AgentGuardrails.Evaluate(state, options));
    }

    [Fact]
    public void Evaluate_WhenOnlyTokenAndTimeAreBlown_ReportsTokenBudgetFirst()
    {
        var options = Defaults;

        var state = new AgentGuardrailState(
            ToolCalls: 0,
            LlmCalls: 0,
            PromptTokens: options.MaxRunTokens + 1,
            CompletionTokens: 0,
            Elapsed: TimeSpan.FromSeconds(options.RunTimeoutSeconds + 1));

        Assert.Equal(AgentStopReason.TokenBudget, AgentGuardrails.Evaluate(state, options));
    }

    // ---- MaxRunLlmCalls (no dedicated stop reason — by design) ---------------

    [Theory]
    [InlineData(0, true)]
    [InlineData(19, true)]
    [InlineData(20, false)]
    [InlineData(21, false)]
    public void CanCallProvider_AllowsExactlyMaxRunLlmCallsRoundTrips(int llmCalls, bool expected)
    {
        // AgentRunLlmCalls is checked BEFORE a provider call and reported as InternalError, because
        // reaching it means the model kept producing turns without finishing (Phase 7 D12).
        var state = new AgentGuardrailState(0, llmCalls, 0, 0, TimeSpan.Zero);

        Assert.Equal(expected, AgentGuardrails.CanCallProvider(state, Defaults));
        Assert.Null(AgentGuardrails.Evaluate(state, Defaults));
    }

    // ---- Describe: tell the Manager which threshold broke, with real numbers --

    [Fact]
    public void Describe_ForToolLimit_NamesTheCapAndTheActualCount()
    {
        var options = Defaults;
        var state = new AgentGuardrailState(17, 0, 0, 0, TimeSpan.Zero);

        var text = AgentGuardrails.Describe(AgentStopReason.ToolLimit, state, options);

        Assert.Contains("17", text, StringComparison.Ordinal);
        Assert.Contains(options.MaxToolCalls.ToString(), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Describe_ForTokenBudget_SplitsPromptAndCompletionTokens()
    {
        var options = Defaults;
        var state = new AgentGuardrailState(0, 0, 40_000, 12_345, TimeSpan.Zero);

        var text = AgentGuardrails.Describe(AgentStopReason.TokenBudget, state, options);

        // The number separator is culture-dependent ("52 345" vs "52345"), so assert on the digits:
        // the contract is "name the numbers actually used, split into the two halves" (D4/D12).
        var digitsOnly = new string(text.Where(char.IsAsciiDigit).ToArray());

        Assert.Contains("40000", digitsOnly, StringComparison.Ordinal);
        Assert.Contains("12345", digitsOnly, StringComparison.Ordinal);
        Assert.Contains(options.MaxRunTokens.ToString(), digitsOnly, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AgentStopReason.ProviderError)]
    [InlineData(AgentStopReason.Cancelled)]
    [InlineData(AgentStopReason.TaskChanged)]
    [InlineData(AgentStopReason.InternalError)]
    [InlineData(AgentStopReason.DraftProduced)]
    [InlineData(AgentStopReason.QuestionAsked)]
    public void Describe_ForEveryStopReason_ReturnsANonEmptyVietnameseExplanation(AgentStopReason reason)
    {
        var text = AgentGuardrails.Describe(
            reason, new AgentGuardrailState(0, 0, 0, 0, TimeSpan.Zero), Defaults);

        Assert.False(string.IsNullOrWhiteSpace(text));
    }

    // ---- defensive clamping (a misconfigured 0 must never disable a guardrail)

    [Fact]
    public void Effective_ClampsZeroOrNegativeThresholdsToAtLeastOne()
    {
        var options = new AgentOptions
        {
            MaxToolCalls = 0,
            RunTimeoutSeconds = -5,
            MaxRunTokens = 0,
            MaxRunLlmCalls = 0,
            MaxDraftChars = 0,
            AttachmentThresholdChars = 0,
            ToolTraceMaxEntries = 0,
        }.Effective;

        Assert.Equal(1, options.MaxToolCalls);
        Assert.Equal(1, options.RunTimeoutSeconds);
        Assert.Equal(1, options.MaxRunTokens);
        Assert.Equal(1, options.MaxRunLlmCalls);
        Assert.Equal(1, options.MaxDraftChars);
        Assert.Equal(1, options.AttachmentThresholdChars);
        Assert.Equal(1, options.ToolTraceMaxEntries);
    }

    [Fact]
    public void Effective_ClampsWebSearchResultsIntoTheSupportedRange()
    {
        Assert.Equal(1, new AgentOptions { WebSearchMaxResults = 0 }.Effective.WebSearchMaxResults);
        Assert.Equal(10, new AgentOptions { WebSearchMaxResults = 999 }.Effective.WebSearchMaxResults);
        Assert.Equal(5, new AgentOptions().Effective.WebSearchMaxResults);
    }

    [Fact]
    public void Effective_FallsBackWhenTheContentTypeOrDefaultsAreBlank()
    {
        var options = new AgentOptions { DefaultAttachmentContentType = "   " }.Effective;

        Assert.Equal("text/markdown", options.DefaultAttachmentContentType);
    }

    [Fact]
    public void RunTimeoutAndOrphanCutoff_DeriveFromTheEffectiveSeconds()
    {
        var options = new AgentOptions { RunTimeoutSeconds = 300, OrphanRunGraceSeconds = 60 };

        Assert.Equal(TimeSpan.FromMinutes(5), options.RunTimeout);
        Assert.Equal(TimeSpan.FromMinutes(6), options.OrphanCutoff);
    }
}
