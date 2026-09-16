using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Periodic driver for the daily digest (Phase 13 §3.3): waits out the configured startup delay, then asks
/// <see cref="IDailyDigestRunner"/> once per poll interval.
/// <para>
/// The poll is deliberately dumb and frequent-ish: <see cref="IDailyDigestRunner"/> decides whether today's
/// digest is due and whether a recipient already has one, so a tick that fires too early or twice is
/// harmless. That is the whole reason the idempotency lives in the database (see the runner's remarks).
/// </para>
/// <para>
/// Deliberately defensive, exactly like <c>ObserverBackgroundService</c>: an unhandled exception escaping a
/// <see cref="BackgroundService"/> stops the whole host (the default
/// <c>BackgroundServiceExceptionBehavior</c> is <c>StopHost</c>), so every tick is wrapped and a failed pass
/// only produces a log entry. A digest is a nice-to-have; it must never be able to take the API down.
/// </para>
/// </summary>
public sealed class DailyDigestBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DigestOptions _options;
    private readonly ILogger<DailyDigestBackgroundService> _logger;

    public DailyDigestBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<DigestOptions> options,
        ILogger<DailyDigestBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Digest background service started (enabled={Enabled}, pollInterval={Interval}, sendAt={Hour:00}:{Minute:00}, "
            + "tzOffset={Offset}, startupDelay={StartupDelay}s, maxUsers={MaxUsers}, maxDailyEmails={MaxEmails}).",
            _options.Enabled,
            _options.PollInterval,
            _options.EffectiveSendAtLocalHour,
            _options.EffectiveSendAtLocalMinute,
            _options.EffectiveTimeZoneOffsetMinutes,
            _options.StartupDelaySeconds,
            _options.EffectiveMaxUsersPerRun,
            _options.EffectiveMaxDailyEmails);

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
            _logger.LogInformation("Digest is disabled (Digest:Enabled=false) — no digest will be sent.");
            return;
        }

        using var timer = new PeriodicTimer(_options.PollInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }

                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let the host die because a digest pass failed; the next tick tries again.
                _logger.LogError(ex, "Digest background pass failed; the timer keeps running.");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        // Scoped services resolve inside their own scope: the runner needs a DbContext and must not hold
        // one for the lifetime of the host (a captive dependency).
        await using var scope = _scopeFactory.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IDailyDigestRunner>();

        await runner.RunOnceAsync(ct);
    }
}
