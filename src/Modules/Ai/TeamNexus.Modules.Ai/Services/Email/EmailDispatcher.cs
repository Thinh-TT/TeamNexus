using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Ai.Services.Email;

/// <summary>
/// The single gateway every transactional email goes through (Phase 11 §2.3): one call = one send
/// attempt + one audit row in <c>email_messages</c>.
/// </summary>
public interface IEmailDispatcher
{
    /// <summary>
    /// Sends one message through the configured transport and records the outcome.
    /// <para>
    /// <b>Never throws.</b> A transport failure becomes a <c>Failed</c> row — the caller's write
    /// (an invitation, a quick email) has already been committed and must not be rolled back
    /// because a provider was unavailable. Same fail-soft rule as <c>IActivityLogWriter</c>.
    /// </para>
    /// </summary>
    /// <param name="bodyPreview">
    /// Short, caller-composed preview for the audit row (≤ 500). Must <b>not</b> contain an
    /// invitation token — see <c>EmailTemplates.InvitationPreview</c>.
    /// </param>
    Task<EmailMessage> SendAsync(
        Guid workspaceId,
        Guid? sentByUserId,
        string kind,
        string toEmail,
        string subject,
        string textBody,
        string htmlBody,
        string bodyPreview,
        CancellationToken ct = default);
}

public sealed class EmailDispatcher : IEmailDispatcher
{
    /// <summary>Matches <c>email_messages.body_preview</c> varchar(500).</summary>
    private const int PreviewLimit = 500;

    /// <summary>Matches <c>email_messages.error</c> varchar(500).</summary>
    private const int ErrorLimit = 500;

    private readonly TeamNexusDbContext _db;
    private readonly IEmailSender _sender;
    private readonly ILogger<EmailDispatcher> _logger;

    public EmailDispatcher(
        TeamNexusDbContext db,
        IEmailSender sender,
        ILogger<EmailDispatcher> logger)
    {
        _db = db;
        _sender = sender;
        _logger = logger;
    }

    public async Task<EmailMessage> SendAsync(
        Guid workspaceId,
        Guid? sentByUserId,
        string kind,
        string toEmail,
        string subject,
        string textBody,
        string htmlBody,
        string bodyPreview,
        CancellationToken ct = default)
    {
        var row = new EmailMessage
        {
            WorkspaceId = workspaceId,
            SentByUserId = sentByUserId,
            Kind = kind,
            ToEmail = toEmail,
            Subject = TruncateRequired(subject, 200),
            BodyPreview = TruncateRequired(bodyPreview, PreviewLimit),
            Status = EmailMessageStatus.Queued,
        };

        _db.EmailMessages.Add(row);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // The audit row itself could not be written: log and report a failed send rather than
            // failing the business operation that triggered it.
            _logger.LogError(ex, "Không ghi được email_messages cho email tới {To}.", toEmail);
            return row;
        }

        EmailSendResult result;
        try
        {
            result = await _sender.SendAsync(
                new EmailEnvelope(toEmail, row.Subject ?? string.Empty, htmlBody, textBody), ct);
        }
        catch (Exception ex)
        {
            // A sender must not throw, but the dispatcher is the last line of defence: an
            // implementation bug must still not escape into the request pipeline.
            result = EmailSendResult.Failed(ex.Message);
        }

        row.Status = result.Success ? EmailMessageStatus.Sent : EmailMessageStatus.Failed;
        row.ProviderMessageId = Truncate(result.ProviderMessageId, 200);
        row.Error = result.Success ? null : Truncate(result.Error, ErrorLimit);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            // Best effort: the transport already has (or rejected) the message, so the outcome is
            // logged even when the audit update fails.
            _logger.LogError(ex, "Không cập nhật được kết quả gửi email cho {To}.", toEmail);
        }

        return row;
    }

    private static string? Truncate(string? value, int max)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= max ? value : value[..max];
    }

    /// <summary>Non-nullable variant for the columns that are <c>NOT NULL</c>.</summary>
    private static string TruncateRequired(string? value, int max)
    {
        var text = value ?? string.Empty;
        return text.Length <= max ? text : text[..max];
    }
}
