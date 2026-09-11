using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// Closes orphaned runs at startup (Phase 7 §4.8, D13): a free-tier app that sleeps or is recycled in
/// the middle of a run leaves a row stuck in <c>Running</c>, which would keep the Kanban card showing
/// "Đang chạy" forever and block every later attempt with a 409.
/// <para>
/// Deliberately a one-shot <see cref="BackgroundService"/> (which IS an <see cref="IHostedService"/>):
/// the work happens once, there is no <c>PeriodicTimer</c>, and — unlike a raw
/// <c>IHostedService.StartAsync</c> — it does not block host startup. Every failure is swallowed, since
/// an unhandled exception in a background service stops the whole host (the
/// <c>ObserverBackgroundService</c> lesson).
/// </para>
/// <para>
/// <b>Not</b> gated by <c>Agent:Enabled</c>: a disabled feature must still get its stuck rows cleaned
/// up, otherwise turning the feature back on starts from a broken state.
/// </para>
/// </summary>
public sealed class AgentRunReaper : BackgroundService
{
    /// <summary>Small delay so startup work (migrations, first requests) is not competing with a bulk update.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AgentOptions _options;
    private readonly ILogger<AgentRunReaper> _logger;

    public AgentRunReaper(
        IServiceScopeFactory scopeFactory,
        IOptions<AgentOptions> options,
        ILogger<AgentRunReaper> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
            await ReapOnceAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Host shutting down before the sweep ran; nothing to clean up in this process.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent run reaper failed; stuck runs (if any) will be closed on the next start.");
        }
    }

    private async Task ReapOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TeamNexusDbContext>();

        var effective = _options.Effective;
        var cutoff = DateTimeOffset.UtcNow - _options.OrphanCutoff;
        var now = DateTimeOffset.UtcNow;

        // Uses the partial index (status) WHERE status = 'Running' — no new index needed (D13).
        var closed = await db.AgentRuns
            .Where(r => r.Status == AgentRunStatus.Running && r.StartedAt < cutoff)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(r => r.Status, AgentRunStatus.Failed)
                .SetProperty(r => r.StopReason, AgentStopReason.InternalError)
                .SetProperty(r => r.Error, "Lượt chạy bị bỏ dở (ứng dụng đã ngủ/khởi động lại) — đóng bởi AgentRunReaper.")
                .SetProperty(r => r.FinishedAt, now)
                .SetProperty(r => r.UpdatedAt, now), ct);

        if (closed > 0)
        {
            _logger.LogWarning(
                "Agent run reaper closed {Count} orphaned run(s) older than {Cutoff} "
                + "(RunTimeoutSeconds={Timeout}s + OrphanRunGraceSeconds={Grace}s).",
                closed,
                cutoff,
                effective.RunTimeoutSeconds,
                effective.OrphanRunGraceSeconds);
        }
        else
        {
            _logger.LogInformation("Agent run reaper found no orphaned run to close.");
        }
    }
}
