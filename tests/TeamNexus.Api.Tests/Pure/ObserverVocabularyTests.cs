using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Ai.Services.Agent;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 8 §2.3 — the severity/type vocabularies shared by the Observer detector and the
/// notification layer. Pure lookups, so this is the cheapest regression net of the phase.
/// </summary>
public sealed class ObserverVocabularyTests
{
    [Fact]
    public void Severities_AreOrderedAscendingFromLowToCritical()
    {
        Assert.Equal(["Low", "Medium", "High", "Critical"], ObserverSeverity.All);
    }

    [Theory]
    [InlineData("Low", 0)]
    [InlineData("Medium", 1)]
    [InlineData("High", 2)]
    [InlineData("Critical", 3)]
    public void Rank_MapsEachKnownSeverityToItsAscendingIndex(string severity, int expected)
    {
        Assert.Equal(expected, NotificationSeverities.Rank(severity));
    }

    [Theory]
    [InlineData("low")]
    [InlineData("  HIGH  ")]
    [InlineData("cRiTiCaL")]
    public void Rank_IsCaseInsensitiveAndTrimsWhitespace(string severity)
    {
        Assert.True(NotificationSeverities.Rank(severity) >= 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Emergency")]
    [InlineData("0")]
    public void Rank_ForUnknownValues_ReturnsMinusOneInsteadOfThrowing(string? severity)
    {
        Assert.Equal(-1, NotificationSeverities.Rank(severity));
    }

    [Fact]
    public void IsKnown_OnlyAcceptsTheFourDocumentedSeverities()
    {
        Assert.True(NotificationSeverities.IsKnown("Critical"));
        Assert.True(NotificationSeverities.IsKnown("  critical  ")); // trimmed, then matched case-insensitively
        Assert.False(NotificationSeverities.IsKnown("Blocker"));
        Assert.False(NotificationSeverities.IsKnown(null));
    }

    [Theory]
    // (severity, minimum, expected)
    [InlineData("Low", "Medium", false)]
    [InlineData("Medium", "Medium", true)]      // the floor itself passes
    [InlineData("High", "Medium", true)]
    [InlineData("Critical", "Medium", true)]
    [InlineData("Medium", "Critical", false)]
    // An unset/unknown floor imposes no minimum, so everything known passes …
    [InlineData("Low", null, true)]
    [InlineData("Low", "", true)]
    [InlineData("Low", "Nonsense", true)]
    // … but an unknown severity always fails, even with no floor.
    [InlineData("Unknown", null, false)]
    [InlineData("Unknown", "Low", false)]
    public void AtLeast_ImplementsTheDocumentedSeverityFloor(string? severity, string? minimum, bool expected)
    {
        Assert.Equal(expected, NotificationSeverities.AtLeast(severity, minimum));
    }

    [Fact]
    public void NotificationTypes_ExposeExactlyTheFourDetectorSignals()
    {
        Assert.Equal(
            ["OverdueTask", "StalledTask", "Overload", "Bottleneck"],
            NotificationTypes.All);
    }

    [Theory]
    [InlineData("overduetask", "OverdueTask")]
    [InlineData(" STALLEDTASK ", "StalledTask")]
    [InlineData("bOtTlEnEcK", "Bottleneck")]
    [InlineData("Bottleneck", "Bottleneck")] // already canonical → returned unchanged
    public void Canonical_NormalizesCasingForKnownTypes(string input, string expected)
    {
        var canonical = NotificationTypes.Canonical(input);

        Assert.Equal(expected, canonical);
        Assert.Contains(canonical!, NotificationTypes.All);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("AgentRunFailed")]
    [InlineData("SomethingElse")]
    public void Canonical_ForUnknownTypes_ReturnsNullSoTheFindingIsDropped(string? input)
    {
        Assert.Null(NotificationTypes.Canonical(input));
        Assert.False(NotificationTypes.IsKnown(input));
    }

    [Fact]
    public void AgentNotificationTypes_AreDeliberatelyOutsideTheObserverWhitelist()
    {
        // The whole point of the separate vocabulary (Phase 7 §4.1): the Observer model must never be
        // able to emit "AgentRunFailed" and pass validation.
        Assert.False(NotificationTypes.IsKnown(AgentNotificationTypes.RunFailed));
        Assert.False(NotificationTypes.IsKnown(AgentNotificationTypes.AwaitingClarification));
        Assert.False(NotificationTypes.IsKnown(AgentNotificationTypes.OutputPending));

        // …and they are still distinct strings from the Observer's own types.
        Assert.DoesNotContain(AgentNotificationTypes.RunFailed, NotificationTypes.All);
    }
}
