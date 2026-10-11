using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// Runs <see cref="AgentRunReaperRunner"/> once at startup (Phase 7 §4.8, D13).
/// <para>
/// Deliberately a one-shot <see cref="BackgroundService"/> (which IS an <see cref="IHostedService"/>): the
/// work happens once, there is no <c>PeriodicTimer</c>, and — unlike a raw <c>IHostedService.StartAsync</c>
/// — it does not block host startup. Every failure is swallowed, since an unhandled exception in a
/// background service stops the whole host (the <c>ObserverBackgroundService</c> lesson).
/// </para>
/// <para>
/// <b>Not</b> gated by <c>Agent:Enabled</c>: a disabled feature must still get its stuck rows cleaned up,
/// otherwise turning the feature back on starts from a broken state.
/// </para>
/// <para>
/// Phase 14.5: the sweep itself moved to <see cref="AgentRunReaperRunner"/> so a test can call it directly
/// — a hosted service cannot be resolved from the container, which made the reaping rules unverifiable
/// except by waiting for a host to start.
/// </para>
/// </summary>
public sealed class AgentRunReaper : BackgroundService
{
    /// <summary>Small delay so startup work (migrations, first requests) is not competing with a bulk update.</summary>
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AgentRunReaper> _logger;

    public AgentRunReaper(
        IServiceScopeFactory scopeFactory,
        ILogger<AgentRunReaper> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            await using var scope = _scopeFactory.CreateAsyncScope();
            var runner = scope.ServiceProvider.GetRequiredService<AgentRunReaperRunner>();
            await runner.RunOnceAsync(ct: stoppingToken);
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
}
