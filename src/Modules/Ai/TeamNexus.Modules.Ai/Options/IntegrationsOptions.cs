namespace TeamNexus.Modules.Ai.Options;

/// <summary>
/// Configuration options for Phase 15 external integrations (Slack, Discord, GitHub).
/// Bound from the <c>"Integrations"</c> configuration section.
/// </summary>
public sealed class IntegrationsOptions
{
    public const string SectionName = "Integrations";

    /// <summary>Maximum number of outbound webhook endpoints allowed per workspace.</summary>
    public int MaxEndpointsPerWorkspace { get; set; } = 10;

    public WebhookOutboxOptions Outbox { get; set; } = new();

    public int EffectiveMaxEndpointsPerWorkspace => Math.Clamp(MaxEndpointsPerWorkspace, 1, 50);
}

public sealed class WebhookOutboxOptions
{
    public bool Enabled { get; set; } = true;

    public int PollIntervalSeconds { get; set; } = 5;

    public int StartupDelaySeconds { get; set; } = 10;

    public int BatchSize { get; set; } = 20;

    public int MaxAttempts { get; set; } = 5;

    public int CircuitBreakerThreshold { get; set; } = 10;

    public int TimeoutSeconds { get; set; } = 10;

    public TimeSpan EffectivePollInterval => TimeSpan.FromSeconds(Math.Clamp(PollIntervalSeconds, 1, 3600));

    public TimeSpan EffectiveStartupDelay => TimeSpan.FromSeconds(Math.Clamp(StartupDelaySeconds, 0, 86400));

    public int EffectiveBatchSize => Math.Clamp(BatchSize, 1, 100);

    public int EffectiveMaxAttempts => Math.Clamp(MaxAttempts, 1, 10);

    public int EffectiveCircuitBreakerThreshold => Math.Clamp(CircuitBreakerThreshold, 1, 100);

    public TimeSpan EffectiveTimeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 1, 60));
}

