using System.Text.Json;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// The activity window one scan considers: either since the previous completed run or, when that
/// is older than the configured lookback, a bounded fallback window (Phase 5 §4.5).
/// </summary>
public sealed record ObserverWindow(DateTimeOffset Start, DateTimeOffset End);

/// <summary>
/// Turns detector output into the small, cost-bounded payload the AI Observer receives
/// (Phase 5 §3.2 / §4.2 — "log được tóm tắt trước khi gửi AI").
/// <para>
/// Pure and deterministic: no DB, no clock, no provider. Everything it needs (including the
/// current time) comes from the snapshot / options, so both the payload shape and the
/// truncation behaviour can be verified without a database or an API key.
/// </para>
/// </summary>
public static class ObserverSummarizer
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Bounded lookback. Three cases, all clamped so the window is never longer than
    /// <c>MaxLookbackDays</c> and never shorter than <c>LookbackHours</c>:
    /// <list type="bullet">
    ///   <item>no completed run yet ⇒ the configured lookback;</item>
    ///   <item>a recent completed run ⇒ continue exactly where it stopped;</item>
    ///   <item>a stale run (host was asleep past <c>MaxLookbackDays</c>) ⇒ look back the maximum,
    ///   not the minimum, so a long outage does not hide activity.</item>
    /// </list>
    /// </summary>
    public static ObserverWindow ComputeWindow(
        DateTimeOffset now, DateTimeOffset? lastCompletedRunFinishedAt, ObserverOptions options)
    {
        var maxLookback = TimeSpan.FromDays(Math.Max(1, options.MaxLookbackDays));
        var floor = now - maxLookback;

        // Clamp(configured lookback, up to MaxLookbackDays) — LookbackHours is a floor, not a cap.
        var boundedLookback = options.LookbackWindow;
        if (boundedLookback > maxLookback)
        {
            boundedLookback = maxLookback;
        }

        var start = lastCompletedRunFinishedAt.HasValue
            ? lastCompletedRunFinishedAt.Value
            : now - boundedLookback;

        if (start < floor)
        {
            start = floor;
        }

        if (start > now)
        {
            start = now - boundedLookback;
        }

        return new ObserverWindow(start, now);
    }

    /// <summary>
    /// Builds the summarized JSON payload. Signals arrive most-severe-first, so when the payload
    /// would exceed <c>MaxPromptCharacters</c> the per-signal task lists are trimmed first and then
    /// the weakest signals are dropped. The result is always valid JSON — a prompt is never cut
    /// mid-structure (token/cost control, tech docs §2.3).
    /// <para>
    /// <b>Truncation order is pinned here, not inherited from the caller</b> (Phase 14 §3.1): the list
    /// is re-sorted by severity then weight before anything is dropped, and only ever from the
    /// <b>end</b>. Without the pin, adding a fifth detector in Phase 14 could have silently starved
    /// <c>AtRiskDeadline</c> out of every large workspace simply because of the order the caller
    /// happened to hand the signals over in.
    /// </para>
    /// </summary>
    public static string BuildPayload(
        ObserverWorkspaceSnapshot snapshot,
        ObserverSignalSet signals,
        ObserverOptions options,
        out int truncatedByPrompt)
    {
        var maxCharacters = Math.Max(1000, options.MaxPromptCharacters);

        var included = signals.Signals
            .OrderByDescending(s => NotificationSeverities.Rank(s.Severity))
            .ThenByDescending(s => s.Weight)
            .ToList();

        // Progressive task limits per signal; the last entry drops task details entirely.
        int[] taskLimits = [40, 10, 3, 0];
        var attempt = 0;

        while (true)
        {
            var payload = Serialize(snapshot, included, signals.TruncatedSignals, options, taskLimits[attempt]);

            if (payload.Length <= maxCharacters || included.Count == 0)
            {
                truncatedByPrompt = signals.Signals.Count - included.Count;
                return payload;
            }

            if (attempt < taskLimits.Length - 1)
            {
                attempt++;
                continue;
            }

            // Already at the smallest shape: drop the weakest signal and retry from the top.
            included.RemoveAt(included.Count - 1);
            attempt = 0;
        }
    }

    /// <summary>Convenience overload for callers that do not need the truncation count.</summary>
    public static string BuildPayload(
        ObserverWorkspaceSnapshot snapshot, ObserverSignalSet signals, ObserverOptions options)
        => BuildPayload(snapshot, signals, options, out _);

    /// <summary>
    /// The completion request for one workspace scan: an observer-marked user prompt (marker on
    /// the first line) carrying the summarized payload, in JSON mode with a cost-bounded token cap.
    /// </summary>
    public static AiCompletionRequest BuildRequest(
        ObserverWorkspaceSnapshot snapshot,
        ObserverSignalSet signals,
        ObserverOptions options)
    {
        var payload = BuildPayload(snapshot, signals, options);
        var userPrompt = $"{ObserverPrompts.AgentMarker}\n{payload}";

        return new AiCompletionRequest(
            ObserverPrompts.BuildSystemPrompt(),
            userPrompt,
            options.Temperature,
            Math.Max(1, options.MaxOutputTokens),
            JsonMode: true);
    }

    /// <summary>
    /// How many signals actually made it into a rendered prompt (Phase 14 §3.1, P4). The run summary
    /// records it next to the number of signals <i>detected</i>: the two only differ when truncation
    /// kicked in, and without this counter a newly added detector could be starved out of every large
    /// workspace without a single visible symptom.
    /// <para>
    /// Defensive by design — an unparsable prompt returns 0 rather than throwing, because a scan must
    /// never fail over a diagnostic counter.
    /// </para>
    /// </summary>
    public static int CountSignalsInPayload(string userPrompt)
    {
        if (string.IsNullOrWhiteSpace(userPrompt))
        {
            return 0;
        }

        var markerIndex = userPrompt.IndexOf(ObserverPrompts.AgentMarker, StringComparison.Ordinal);
        var json = markerIndex < 0
            ? userPrompt
            : userPrompt[(markerIndex + ObserverPrompts.AgentMarker.Length)..];

        try
        {
            using var document = JsonDocument.Parse(json.TrimStart());
            return document.RootElement.TryGetProperty("signals", out var signals)
                   && signals.ValueKind == JsonValueKind.Array
                ? signals.GetArrayLength()
                : 0;
        }
        catch (JsonException)
        {
            return 0;
        }
    }

    // ---- internals ---------------------------------------------------------

    private static string Serialize(
        ObserverWorkspaceSnapshot snapshot,
        IReadOnlyList<ObserverSignal> signals,
        int truncatedSignals,
        ObserverOptions options,
        int maxTasksPerSignal)
    {
        var titleLength = Math.Max(1, options.MaxTitleExcerptLength);

        var tasksById = new Dictionary<Guid, ObserverTaskSnapshot>();
        foreach (var task in snapshot.Tasks)
        {
            tasksById[task.TaskId] = task;
        }

        var users = snapshot.Tasks
            .Where(t => t.AssigneeId.HasValue && !string.IsNullOrWhiteSpace(t.AssigneeName))
            .GroupBy(t => t.AssigneeId!.Value)
            .ToDictionary(g => g.Key, g => g.First().AssigneeName!);

        var now = snapshot.Now;

        var payload = new
        {
            agent = "observer",
            workspaceId = snapshot.WorkspaceId,
            now,
            signalCount = signals.Count,
            truncatedSignals,
            signals = signals.Select(signal => new
            {
                signal.Type,
                signal.Severity,
                signal.Weight,
                signal.Summary,
                evidence = new { taskIds = signal.TaskIds, userIds = signal.UserIds },
                tasks = maxTasksPerSignal <= 0
                    ? new List<object>()
                    : signal.TaskIds
                        .Where(tasksById.ContainsKey)
                        .Take(maxTasksPerSignal)
                        .Select(taskId => Describe(tasksById[taskId], now, titleLength))
                        .ToList(),
            }).ToList(),
            users,
        };

        return JsonSerializer.Serialize(payload, Json);
    }

    /// <summary>Short, non-sensitive task context (no description — token/cost control).</summary>
    private static object Describe(ObserverTaskSnapshot task, DateTimeOffset now, int titleLength) => new
    {
        id = task.TaskId,
        title = Excerpt(task.Title, titleLength),
        board = task.BoardName,
        column = task.ColumnName,
        dueDate = task.DueDate,
        // Deliberately double: a task years past due must not overflow an int here.
        daysOverdue = task.DueDate.HasValue ? (now - task.DueDate.Value).TotalDays : (double?)null,
        daysIdle = (now - ObserverSignalDetector.ActivityAt(task)).TotalDays,
        assigneeId = task.AssigneeId,
        comments = task.CommentCount,
    };

    private static string Excerpt(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength];
}
