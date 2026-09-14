using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Ai.Services.Email;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Adapter that fulfils the Board module's <see cref="IEmailGateway"/> port (Phase 11 §2/§3).
/// <para>
/// Board owns the invitation lifecycle but must not know how email is rendered or delivered; this
/// class is the seam. It composes the message with the pure <see cref="EmailTemplates"/>, sends it
/// through <see cref="IEmailDispatcher"/> (which writes the <c>email_messages</c> audit row), and
/// reports only a boolean — the inviter needs to know whether the link went out, not the provider's
/// message id.
/// </para>
/// <para>
/// <b>Never throws.</b> The port contract requires it: the invitation row is already committed when
/// this is called, and a provider outage must not fail the request that created it.
/// </para>
/// </summary>
public sealed class EmailGateway : IEmailGateway
{
    private readonly IEmailDispatcher _dispatcher;
    private readonly ILogger<EmailGateway> _logger;

    public EmailGateway(IEmailDispatcher dispatcher, ILogger<EmailGateway> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<bool> SendInvitationAsync(
        Guid workspaceId,
        Guid sentByUserId,
        string toEmail,
        string workspaceName,
        string invitedByName,
        string role,
        string acceptUrl,
        int expiryDays,
        CancellationToken ct = default)
    {
        var (subject, text, html) = EmailTemplates.Invitation(
            workspaceName, invitedByName, role, acceptUrl, expiryDays);

        // The preview deliberately carries the workspace name only — never the body, because the
        // body contains the raw accept token and email_messages is a queryable column.
        var preview = EmailTemplates.InvitationPreview(workspaceName);

        var row = await _dispatcher.SendAsync(
            workspaceId,
            sentByUserId,
            EmailKinds.WorkspaceInvitation,
            toEmail,
            subject,
            text,
            html,
            preview,
            ct);

        return row.Status == EmailMessageStatus.Sent;
    }

    public async Task<bool> SendQuickEmailAsync(
        Guid workspaceId,
        Guid sentByUserId,
        string toEmail,
        string workspaceName,
        string senderName,
        string subject,
        string body,
        CancellationToken ct = default)
    {
        var (renderedSubject, text, html) = EmailTemplates.Quick(workspaceName, senderName, subject, body);

        var row = await _dispatcher.SendAsync(
            workspaceId,
            sentByUserId,
            EmailKinds.QuickEmail,
            toEmail,
            renderedSubject,
            text,
            html,
            EmailTemplates.QuickPreview(body),
            ct);

        return row.Status == EmailMessageStatus.Sent;
    }
}
