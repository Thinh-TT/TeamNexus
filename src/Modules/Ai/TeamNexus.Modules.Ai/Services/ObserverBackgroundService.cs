using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Periodic driver for the AI Observer (Phase 5 §4.6): waits out the configured startup delay,
/// then scans all workspaces once per <c>Observer:IntervalMinutes</c>.
/// <para>
/// Deliberately defensive: an unhandled exception escaping a <see cref="BackgroundService"/>
/// stops the whole host (the default <c>BackgroundServiceExceptionBehavior</c> is
/// <c>StopHost</c>), so every tick is wrapped and a failed scan only produces a log entry.
/// </para>
/// </summary>
public sealed class ObserverBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ObserverOptions _options;
    private readonly ILogger<ObserverBackgroundService> _logger;

    public ObserverBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<ObserverOptions> options,
        ILogger<ObserverBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Observer background service started (enabled={Enabled}, interval={Interval}, "
            + "startupDelay={StartupDelaySeconds}s, lookback={LookbackHours}h, dedupe={DedupeWindowHours}h).",
            _options.Enabled,
            _options.Interval,
            _options.StartupDelaySeconds,
            _options.LookbackHours,
            _options.DeduplicationWindowHours);

        try
        {
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(0, _options.StartupDelaySeconds)), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!_options.Enabled)
        {
            _logger.LogInformation("Observer is disabled (Observer:Enabled=false) — periodic scan will not run.");
            return;
        }

        using var timer = new PeriodicTimer(_options.Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }

                await ScanOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let the host die because a scan failed; the next tick tries again.
                _logger.LogError(ex, "Observer background scan failed; the timer keeps running.");
            }
        }
    }

    private async Task ScanOnceAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var observer = scope.ServiceProvider.GetRequiredService<IObserverService>();

        // No acting user: the timer scans on behalf of the system (all active workspaces).
        await observer.ScanAsync(null, null, ct);
    }
}
