using TeamNexus.Modules.Ai.Services.Integrations;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// An <see cref="IExternalAlertSink"/> that records what it was offered (Phase 14.5 A1.2).
/// <para>
/// The seam exists so Phase 15 can add Slack/Discord/webhook transports without editing a single producer.
/// This double is how that claim is verified today: it stands exactly where a real transport will, and a
/// suite can then assert that the Observer, the agent and the member-alert paths all reach it.
/// </para>
/// <para>
/// <b>Never throws</b> — the same contract the real implementations must honour, and the reason a broken
/// transport degrades to "in-app only" instead of failing the request that produced the alert.
/// </para>
/// </summary>
public sealed class RecordingExternalAlertSink : IExternalAlertSink
{
    private readonly List<ExternalAlertMessage> _messages = [];

    /// <summary>Everything offered to the sink, in order.</summary>
    public IReadOnlyList<ExternalAlertMessage> Messages => _messages;

    public Task PublishAsync(ExternalAlertMessage message, CancellationToken ct = default)
    {
        lock (_messages)
        {
            _messages.Add(message);
        }

        return Task.CompletedTask;
    }

    /// <summary>Awaits nothing but makes the "recorded so far" read obviously ordered against writes.</summary>
    public bool HasType(string type)
    {
        lock (_messages)
        {
            return _messages.Any(m => string.Equals(m.Type, type, StringComparison.Ordinal));
        }
    }
}
