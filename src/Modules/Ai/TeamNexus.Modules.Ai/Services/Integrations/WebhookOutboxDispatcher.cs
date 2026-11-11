using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Ai.Options;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Thin <see cref="BackgroundService"/> wrapper that periodically invokes <see cref="WebhookOutboxRunner"/>
/// to dispatch pending Slack/Discord webhooks (Phase 15.2 §T15.2.3).
/// </summary>
public sealed class WebhookOutboxDispatcher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IntegrationsOptions _options;
    private readonly ILogger<WebhookOutboxDispatcher> _logger;

    public WebhookOutboxDispatcher(
        IServiceScopeFactory scopeFactory,
        IOptions<IntegrationsOptions> options,
        ILogger<WebhookOutboxDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Outbox.Enabled)
        {
            _logger.LogInformation("WebhookOutboxDispatcher is disabled via configuration.");
            return;
        }

        var startupDelay = _options.Outbox.EffectiveStartupDelay;
        if (startupDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(startupDelay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }

        var interval = _options.Outbox.EffectivePollInterval;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<WebhookOutboxRunner>();
                await runner.ProcessBatchAsync(ct: stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "WebhookOutboxDispatcher encountered an error during outbox sweep.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}

