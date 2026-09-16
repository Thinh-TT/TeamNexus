using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Ai.Services.Email;
using TeamNexus.Modules.Board.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services;

/// <summary>
/// Renders and sends one daily digest (Phase 13 §3.3).
/// <para>
/// The Ai module owns this because it owns the wording and the transport: Board assembles the content
/// (<see cref="IDailyDigestService"/>) and this class turns it into an e-mail through the single
/// <see cref="IEmailDispatcher"/> gateway, which writes the <c>email_messages</c> audit row.
/// </para>
/// <para>
/// <b>Never throws.</b> A provider outage must not abort the rest of the run — the dispatcher already
/// records the failure as a row, and the runner only cares how many went out.
/// </para>
/// </summary>
public interface IDigestGateway
{
    /// <summary>Sends one digest and reports whether the transport accepted it.</summary>
    Task<bool> SendDailyDigestAsync(DailyDigestContent content, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class DigestGateway : IDigestGateway
{
    private readonly IEmailDispatcher _dispatcher;
    private readonly ILogger<DigestGateway> _logger;

    public DigestGateway(IEmailDispatcher dispatcher, ILogger<DigestGateway> logger)
    {
        _dispatcher = dispatcher;
        _logger = logger;
    }

    public async Task<bool> SendDailyDigestAsync(DailyDigestContent content, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        try
        {
            var (subject, text, html) = EmailTemplates.DailyDigest(content);

            // `sentByUserId` is null: nobody pressed a button. That is what the column means for a
            // scheduled send, and it is what distinguishes a digest row from a quick email.
            // `workspaceId` is the first section's workspace — the column is NOT NULL, the digest spans
            // several, and the first one is deterministic (sections are ordered by joined_at).
            var row = await _dispatcher.SendAsync(
                content.Workspaces[0].Dashboard.WorkspaceId,
                sentByUserId: null,
                EmailKinds.DailyDigest,
                content.RecipientEmail,
                subject,
                text,
                html,
                EmailTemplates.DailyDigestPreview(content.Workspaces.Count, content.SendDateLocal),
                ct);

            return row.Status == EmailMessageStatus.Sent;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The dispatcher is not supposed to throw, but this is the last line of defence: one bad
            // recipient must not take down the whole run.
            _logger.LogError(ex, "Không gửi được email tóm tắt hằng ngày.");
            return false;
        }
    }
}
