using System.Collections.Concurrent;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// In-process bookkeeping for running agents (Phase 7 §4.9/D11): a per-task reservation and the
/// cancellation handle of every live run.
/// <para>
/// <b>Why per-task reservations and not the Observer's global <c>SemaphoreSlim(1,1)</c>:</b> the
/// Observer may only scan once at a time globally, but two DIFFERENT tasks must be able to run at the
/// same time — a global gate would reject the second task with a bogus 409. The database advisory
/// lock remains the cross-instance guard (see <c>AgentRunOrchestrator</c>).
/// </para>
/// </summary>
public sealed class AgentRunCancellationRegistry
{
    private readonly ConcurrentDictionary<Guid, byte> _tasksInFlight = new();

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _runs = new();

    /// <summary>Number of live runs in this process — diagnostics only.</summary>
    public int LiveRunCount => _runs.Count;

    /// <summary>
    /// Claims a task for a new run. <c>false</c> means another run for the same task is already being
    /// started in this process, which is what turns a double-click into a deterministic 409.
    /// </summary>
    public bool TryReserveTask(Guid taskId) => _tasksInFlight.TryAdd(taskId, 0);

    public void ReleaseTask(Guid taskId) => _tasksInFlight.TryRemove(taskId, out _);

    /// <summary>Registers the cancellation handle of a run. <c>false</c> when the run is already registered.</summary>
    public bool Register(Guid runId, CancellationTokenSource cts) => _runs.TryAdd(runId, cts);

    /// <summary>Requests cancellation. <c>false</c> when this process is not the one running it.</summary>
    public bool TryCancel(Guid runId)
    {
        if (!_runs.TryGetValue(runId, out var cts))
        {
            return false;
        }

        try
        {
            cts.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            // The run finished between the lookup and the cancel: treated as "not running anymore".
            _runs.TryRemove(runId, out _);
            return false;
        }
    }

    /// <summary>Removes and returns the handle so the owner can dispose it.</summary>
    public CancellationTokenSource? Take(Guid runId)
        => _runs.TryRemove(runId, out var cts) ? cts : null;

    public bool IsRunning(Guid runId) => _runs.ContainsKey(runId);
}
