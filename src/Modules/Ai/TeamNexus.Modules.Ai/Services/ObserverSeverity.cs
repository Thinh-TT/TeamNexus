namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Severity levels shared by the AI Observer's detected signals (Phase 5 §3) and the
/// notifications derived from them (Phase 5 §4). Stored as free text (DB design §4) — the
/// database only ever uses it inside <c>notifications.payload</c>, never as a column with a CHECK.
/// </summary>
public static class ObserverSeverity
{
    public const string Low = "Low";

    public const string Medium = "Medium";

    public const string High = "High";

    public const string Critical = "Critical";

    /// <summary>All known severities, ascending — index equals <see cref="NotificationSeverities.Rank"/>.</summary>
    public static IReadOnlyList<string> All { get; } = [Low, Medium, High, Critical];
}

/// <summary>
/// The single source of truth for severity ordering, used by the detector (sorting signals, §3)
/// and the notification layer (filtering by <c>MinSeverityToNotify</c>, §4).
/// <para>
/// Ordering is ascending — <c>Low &lt; Medium &lt; High &lt; Critical</c> — and every lookup is
/// case-insensitive; unknown values rank <c>-1</c> so they are filtered out rather than throwing.
/// </para>
/// </summary>
public static class NotificationSeverities
{
    /// <summary>All known severities, ascending (index == rank).</summary>
    public static IReadOnlyList<string> All => ObserverSeverity.All;

    /// <summary>Rank 0..3 (higher = more severe); <c>-1</c> when the value is null/unknown.</summary>
    public static int Rank(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
        {
            return -1;
        }

        var value = severity.Trim();

        for (var i = 0; i < ObserverSeverity.All.Count; i++)
        {
            if (string.Equals(ObserverSeverity.All[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>True when the value is one of the four known severities.</summary>
    public static bool IsKnown(string? severity) => Rank(severity) >= 0;

    /// <summary>
    /// True when <paramref name="severity"/> is at least <paramref name="minimum"/>. An unknown
    /// severity always fails; an unset/unknown minimum imposes no floor (everything known passes).
    /// Used to drop low-signal findings before any notification row is written (§4.4).
    /// </summary>
    public static bool AtLeast(string? severity, string? minimum)
    {
        var actual = Rank(severity);
        if (actual < 0)
        {
            return false;
        }

        var floor = Rank(minimum);

        return floor < 0 || actual >= floor;
    }
}

/// <summary>
/// Alert/signal type names. One vocabulary for the whole Observer pipeline: the activity
/// detector produces them (§3), the AI may only echo them back (§4.2) and they become
/// <c>notifications.type</c> (§4.4). Stored as free text (DB design §4).
/// </summary>
public static class NotificationTypes
{
    public const string OverdueTask = ObserverSignalDetector.OverdueTask;

    public const string StalledTask = ObserverSignalDetector.StalledTask;

    public const string Overload = ObserverSignalDetector.Overload;

    public const string Bottleneck = ObserverSignalDetector.Bottleneck;

    /// <summary>
    /// Phase 14 §3.1 — "task chưa done, còn rất ít thời gian nhưng không ai động tới". Placed before
    /// <see cref="Overload"/>/<see cref="Bottleneck"/> in <see cref="All"/> purely for readability; the
    /// order in this list has no behavioural effect (severity, not list position, drives ordering).
    /// </summary>
    public const string AtRiskDeadline = ObserverSignalDetector.AtRiskDeadline;

    /// <summary>
    /// All known types, in detector order. Phase 14 grew this from four to five: it is the
    /// <b>anti-hallucination whitelist</b> for the Observer prompt, so a type missing here is a type the
    /// model can never legitimately report.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
        [OverdueTask, StalledTask, AtRiskDeadline, Overload, Bottleneck];

    /// <summary>Case-insensitive membership test (AI output is untrusted).</summary>
    public static bool IsKnown(string? type)
        => !string.IsNullOrWhiteSpace(type)
           && All.Any(t => string.Equals(t, type.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Canonical casing for a known type, or <c>null</c> when unknown.</summary>
    public static string? Canonical(string? type)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var value = type.Trim();
        return All.FirstOrDefault(t => string.Equals(t, value, StringComparison.OrdinalIgnoreCase));
    }
}
