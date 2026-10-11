namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// One alert that has just been persisted as <c>notifications</c> rows, in the provider-neutral shape an
/// outbound integration (Slack, Discord, a webhook) would send.
/// <para>
/// Deliberately <b>not</b> the EF <c>Notification</c> entity: the whole point of the seam is that a
/// transport must be implementable without the persistence model — and without being able to read or
/// write the database at all.
/// </para>
/// </summary>
/// <param name="WorkspaceId">Workspace the alert belongs to.</param>
/// <param name="Type">
/// Notification type exactly as stored (<c>OverdueTask</c>, <c>AgentRunFailed</c>, <c>TaskAssigned</c>, …),
/// so a transport can route on it without duplicating the vocabulary.
/// </param>
/// <param name="Title">Short human title, already truncated by <c>NotificationService</c>.</param>
/// <param name="Message">Body, already truncated by <c>NotificationService</c>.</param>
/// <param name="PayloadJson">Raw jsonb payload, or <c>null</c>. Passed through untouched.</param>
/// <param name="RecipientUserIds">
/// The recipients the in-app rows were written for. A workspace-level transport (one Slack channel per
/// workspace) can ignore this; a direct-message transport needs it.
/// </param>
public sealed record ExternalAlertMessage(
    Guid WorkspaceId,
    string Type,
    string Title,
    string Message,
    string? PayloadJson,
    IReadOnlyList<Guid> RecipientUserIds);

/// <summary>
/// Outbound seam for alerts (Phase 14.5 §Track A / A1.2): every alert that becomes an in-app
/// <c>notifications</c> row is also offered to an external transport.
/// <para>
/// <b>Why this exists now, before any transport does.</b> Phase 15 adds Slack/Discord/GitHub
/// notifications. Without a seam, that phase would have to edit the Observer pipeline, the Agent
/// orchestrator and the member-alert path <i>separately</i> — three places, three chances to miss one.
/// With the seam, Phase 15 registers one implementation and touches <b>no</b> producer.
/// </para>
/// <para>
/// <b>All four write paths already funnel through the three methods of <c>NotificationService</c></b>
/// (<c>NotifyManagersAsync</c> ×2 and <c>NotifyUsersAsync</c>), and <c>NotificationWriter</c> is only a
/// thin adapter over one of them — so hooking the service, not the adapter, is what makes the seam
/// complete. That placement is the reason this port was worth adding at all.
/// </para>
/// <para>
/// <b>Never throws.</b> The same contract as the notification path it hangs off: the task, comment or
/// agent run that triggered the alert has already committed, and a transport's outage must not turn a
/// successful write into a 500. An implementation that cannot publish must swallow and log.
/// </para>
/// </summary>
public interface IExternalAlertSink
{
    /// <summary>
    /// Offers one persisted alert to the transport. Called after the in-app rows are committed, so an
    /// implementation never sees an alert that does not exist.
    /// </summary>
    Task PublishAsync(ExternalAlertMessage message, CancellationToken ct = default);
}

/// <summary>
/// The default registration, and the only one until Phase 15: writes nothing.
/// <para>
/// It exists as a named type rather than "no registration" so the dependency is explicit in
/// <c>AiModule</c> and a test can swap it for a recording double. It is intentionally <b>not</b> named
/// "Null" in the sense of "disabled feature": with no transport configured, the in-app notification is
/// the whole feature, and that is the intended production behaviour — not a degraded mode.
/// </para>
/// </summary>
public sealed class NoOpExternalAlertSink : IExternalAlertSink
{
    public Task PublishAsync(ExternalAlertMessage message, CancellationToken ct = default)
        => Task.CompletedTask;
}
