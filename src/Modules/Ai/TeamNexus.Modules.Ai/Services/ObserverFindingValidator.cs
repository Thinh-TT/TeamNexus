using TeamNexus.Modules.Ai.Contracts;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// A validated, persisted-ready alert derived from one AI finding (Phase 5 §4.2).
/// Evidence ids have already been intersected with the detector's evidence, so they can only
/// ever point at tasks/users the system actually found.
/// </summary>
public sealed record ObserverFinding(
    string Type,
    string Severity,
    string Title,
    string Message,
    IReadOnlyList<Guid> TaskIds,
    IReadOnlyList<Guid> UserIds);

/// <summary>
/// Turns untrusted AI output into trusted findings (Phase 5 §4.2). Pure, deterministic and
/// completely defensive — this is the anti-hallucination boundary:
/// <list type="bullet">
///   <item>an unknown <c>type</c> or a severity below <c>MinSeverityToNotify</c> drops the finding;</item>
///   <item>evidence ids are <b>intersected</b> with the matching signal's ids, so invented ids vanish;</item>
///   <item>missing/overlong text is defaulted/capped instead of failing the whole run.</item>
/// </list>
/// </summary>
public static class ObserverFindingValidator
{
    public const int MaxTitleLength = 200;

    public const int MaxMessageLength = 2000;

    public static IReadOnlyList<ObserverFinding> Validate(
        AiObserverOutput? output,
        IReadOnlyList<ObserverSignal> signals,
        ObserverOptions options)
    {
        if (output?.Findings is null || output.Findings.Count == 0 || signals.Count == 0)
        {
            return [];
        }

        var maxEvidence = Math.Max(1, options.MaxEvidenceIdsPerSignal);
        var findings = new List<ObserverFinding>();

        foreach (var raw in output.Findings)
        {
            // Bounds the total: the model can never produce more findings than the signals
            // that justified the call (one finding per signal at most).
            if (findings.Count >= signals.Count)
            {
                break;
            }

            var type = NotificationTypes.Canonical(raw.Type);
            if (type is null)
            {
                continue;
            }

            var severity = NotificationSeverities.IsKnown(raw.Severity)
                ? NotificationSeverities.All[NotificationSeverities.Rank(raw.Severity)]
                : ObserverSeverity.Medium;

            if (!NotificationSeverities.AtLeast(severity, options.MinSeverityToNotify))
            {
                continue;
            }

            // Evidence must be a subset of the signal of the same type — never widened.
            var signal = signals.FirstOrDefault(s => string.Equals(s.Type, type, StringComparison.OrdinalIgnoreCase));
            if (signal is null)
            {
                continue;
            }

            var taskIds = Intersect(raw.TaskIds, signal.TaskIds, maxEvidence);
            var userIds = Intersect(raw.UserIds, signal.UserIds, maxEvidence);

            var title = string.IsNullOrWhiteSpace(raw.Title)
                ? $"{type} alert"
                : Cap(raw.Title.Trim(), MaxTitleLength);

            var message = string.IsNullOrWhiteSpace(raw.Message)
                ? signal.Summary
                : Cap(raw.Message.Trim(), MaxMessageLength);

            findings.Add(new ObserverFinding(type, severity, title, message, taskIds, userIds));
        }

        return findings;
    }

    /// <summary>Keeps only ids present in <paramref name="allowed"/>, preserving the AI's order.</summary>
    private static IReadOnlyList<Guid> Intersect(IReadOnlyList<Guid>? proposed, IReadOnlyList<Guid> allowed, int max)
    {
        if (proposed is null || proposed.Count == 0 || allowed.Count == 0)
        {
            return [];
        }

        var allowedSet = new HashSet<Guid>(allowed);
        var result = new List<Guid>();

        foreach (var id in proposed)
        {
            if (result.Count >= max)
            {
                break;
            }

            if (allowedSet.Contains(id) && !result.Contains(id))
            {
                result.Add(id);
            }
        }

        return result;
    }

    private static string Cap(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
