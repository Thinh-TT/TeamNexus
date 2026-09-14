namespace TeamNexus.Modules.Board.Services;

/// <summary>
/// Output port for the workspace-invitation email (Phase 11 §3.2, decision D9).
/// <para>
/// The interface deliberately lives in the <b>Board</b> module: Board must not reference the Ai
/// module (Ai already references Board), so Board declares the port and module Ai supplies the real
/// implementation that renders the template and writes the <c>email_messages</c> audit row.
/// <c>AddBoardModule</c> registers a no-op so the Board module stays self-sufficient; Ai overrides it.
/// </para>
/// <para>
/// <b>Implementations must never throw.</b> Sending is a best-effort side effect of a write that has
/// already been committed: the invitation row is real business data and a provider outage must not
/// undo it (same fail-soft rule as <see cref="IActivityLogWriter"/>).
/// </para>
/// </summary>
public interface IEmailGateway
{
    /// <summary>
    /// Sends the invitation email for an existing <c>Pending</c> invitation.
    /// </summary>
    /// <param name="acceptUrl">
    /// The full accept link, including the <b>raw</b> token. This is the only place the raw value
    /// exists outside the email body — it is never persisted, logged, or returned by the API.
    /// </param>
    /// <returns><c>true</c> when the provider accepted the message; <c>false</c> otherwise.</returns>
    Task<bool> SendInvitationAsync(
        Guid workspaceId,
        Guid sentByUserId,
        string toEmail,
        string workspaceName,
        string invitedByName,
        string role,
        string acceptUrl,
        int expiryDays,
        CancellationToken ct = default);

    /// <summary>
    /// Sends one "quick email" composed by a Manager (Phase 11 §4.1), writing its own audit row.
    /// </summary>
    /// <returns><c>true</c> when the provider accepted the message; <c>false</c> otherwise.</returns>
    Task<bool> SendQuickEmailAsync(
        Guid workspaceId,
        Guid sentByUserId,
        string toEmail,
        string workspaceName,
        string senderName,
        string subject,
        string body,
        CancellationToken ct = default);
}

/// <summary>
/// No-op gateway used when the Ai module is not registered (module-scoped tests/harnesses) and as
/// the Board module's own default registration.
/// <para>It reports "not sent" so a missing email adapter is visible in the API response instead of
/// being silently presented as a delivered email.</para>
/// </summary>
public sealed class NullEmailGateway : IEmailGateway
{
    public Task<bool> SendInvitationAsync(
        Guid workspaceId,
        Guid sentByUserId,
        string toEmail,
        string workspaceName,
        string invitedByName,
        string role,
        string acceptUrl,
        int expiryDays,
        CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> SendQuickEmailAsync(
        Guid workspaceId,
        Guid sentByUserId,
        string toEmail,
        string workspaceName,
        string senderName,
        string subject,
        string body,
        CancellationToken ct = default)
        => Task.FromResult(false);
}
