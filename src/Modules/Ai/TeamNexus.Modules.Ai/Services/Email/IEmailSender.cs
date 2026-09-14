using Microsoft.Extensions.Logging;

namespace TeamNexus.Modules.Ai.Services.Email;

/// <summary>
/// One transactional email, fully composed and ready for a transport.
/// <para>
/// Both bodies are built by <see cref="EmailTemplates"/>: <see cref="TextBody"/> is the plain-text
/// fallback and <see cref="HtmlBody"/> the HTML alternative. Every interpolated value is
/// HTML-encoded there, so a workspace named <c>&lt;script&gt;</c> can never inject markup.
/// </para>
/// </summary>
public sealed record EmailEnvelope(string To, string Subject, string HtmlBody, string TextBody);

/// <summary>
/// Outcome of one send attempt. <see cref="ProviderMessageId"/> is null exactly when
/// <see cref="Success"/> is false; <see cref="Error"/> carries the provider's own message.
/// </summary>
public sealed record EmailSendResult(bool Success, string? ProviderMessageId, string? Error)
{
    public static EmailSendResult Sent(string? providerMessageId)
        => new(true, providerMessageId, null);

    public static EmailSendResult Failed(string error)
        => new(false, null, error);
}

/// <summary>
/// Email transport (Phase 11 §2.2). Pure delivery: a sender never touches the database — the audit
/// row is written by <see cref="IEmailDispatcher"/> around this call.
/// <para>
/// <b>Implementations must never throw.</b> A transport failure is a <c>Failed</c> result, not an
/// exception: sending mail is a side effect of a write that has already been committed (the same
/// fail-soft rule as <c>IActivityLogWriter</c> and <c>IBoardEventPublisher</c>).
/// </para>
/// </summary>
public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken ct = default);
}

/// <summary>
/// Offline sender used when no <c>Email:ApiKey</c> is configured (dev, a fresh clone, every CI run).
/// It logs the recipient and subject — never the body, which can contain an invitation token — and
/// reports success so the surrounding flow (audit row, UI feedback) behaves exactly as in
/// production. The same deliberate choice as <c>FakeAiProvider</c> / <c>FakeWebSearchProvider</c>.
/// </summary>
public sealed class NullEmailSender : IEmailSender
{
    private readonly ILogger<NullEmailSender> _logger;

    public NullEmailSender(ILogger<NullEmailSender> logger)
    {
        _logger = logger;
    }

    public Task<EmailSendResult> SendAsync(EmailEnvelope envelope, CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Email:ApiKey chưa được cấu hình — NullEmailSender bỏ qua email tới {To} (subject={Subject}). "
            + "Đặt key bằng: dotnet user-secrets set --project src/TeamNexus.Api \"Email:ApiKey\" \"re_...\"",
            envelope.To,
            envelope.Subject);

        return Task.FromResult(EmailSendResult.Sent(null));
    }
}
