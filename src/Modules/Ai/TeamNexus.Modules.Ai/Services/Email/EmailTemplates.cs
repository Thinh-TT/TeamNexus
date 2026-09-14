using System.Net;
using System.Text.Encodings.Web;
using System.Text.Unicode;

namespace TeamNexus.Modules.Ai.Services.Email;

/// <summary>
/// Email kinds written to <c>email_messages.kind</c> (free text — DB design §4).
/// </summary>
public static class EmailKinds
{
    /// <summary>Workspace invitation with an accept link (Phase 11 ô A).</summary>
    public const string WorkspaceInvitation = "WorkspaceInvitation";

    /// <summary>Short message a Manager composes and sends to members (Phase 11 ô B).</summary>
    public const string QuickEmail = "QuickEmail";
}

/// <summary>
/// Renders the two email kinds this phase sends (Phase 11 §2.4).
/// <para>
/// <b>Pure functions</b> — no EF, no I/O, no clock — so the wording and the escaping rules are
/// verifiable by a plain unit test (see <c>Pure/EmailTemplateTests.cs</c>).
/// </para>
/// <para>
/// <b>Every interpolated value is HTML-encoded.</b> A workspace name, a sender display name, a
/// subject and a body are all user-controlled, so an unencoded <c>&lt;script&gt;</c> would render as
/// markup in the recipient's mail client. <see cref="WebUtility.HtmlEncode"/> is the structural
/// guarantee, not a nicety.
/// </para>
/// </summary>
public static class EmailTemplates
{
    /// <summary>Subject column is <c>varchar(200)</c> (email_messages.subject).</summary>
    public const int MaxSubjectLength = 200;

    /// <summary>CTA prefix keeps the plain-text body readable in a terminal / plain reader.</summary>
    private const string HtmlTemplate = """
        <!DOCTYPE html>
        <html lang="vi">
          <body style="margin:0;padding:24px;background:#f8fafc;font-family:Segoe UI,Arial,sans-serif;color:#0f172a;">
            <div style="max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #e2e8f0;border-radius:12px;padding:24px;">
              <h2 style="margin:0 0 8px;font-size:18px;color:#6366f1;">TeamNexus</h2>
              {BODY}
              <hr style="margin:24px 0;border:none;border-top:1px solid #e2e8f0;" />
              <p style="margin:0;font-size:12px;color:#64748b;">
                Email tự động từ TeamNexus — vui lòng không trả lời trực tiếp email này.
              </p>
            </div>
          </body>
        </html>
        """;

    /// <summary>
    /// Invitation email: workspace, inviter, the role that will be granted, the expiry and the
    /// accept link. <paramref name="acceptUrl"/> carries the raw token — the only place it exists.
    /// </summary>
    public static (string Subject, string TextBody, string HtmlBody) Invitation(
        string workspaceName,
        string invitedByName,
        string role,
        string acceptUrl,
        int expiryDays)
    {
        var safeWorkspace = Encode(workspaceName);
        var safeInviter = Encode(invitedByName);
        var safeRole = Encode(role);
        var safeUrl = Encode(acceptUrl);

        var subject = Truncate($"Lời mời tham gia workspace \"{workspaceName}\" trên TeamNexus");

        var text = $"""
            Xin chào,

            {invitedByName} đã mời bạn tham gia workspace "{workspaceName}" trên TeamNexus
            với vai trò {role}.

            Nhấn vào liên kết dưới đây để tham gia (hết hạn sau {expiryDays} ngày):

            {acceptUrl}

            Nếu bạn không mong đợi lời mời này, hãy bỏ qua email.
            """;

        var body = $"""
              <h3 style="margin:0 0 12px;font-size:16px;">Bạn được mời tham gia một workspace</h3>
              <p style="margin:0 0 12px;line-height:1.6;">
                <strong>{safeInviter}</strong> đã mời bạn tham gia workspace
                <strong>{safeWorkspace}</strong> với vai trò <strong>{safeRole}</strong>.
              </p>
              <p style="margin:0 0 20px;line-height:1.6;">
                Liên kết hết hạn sau <strong>{expiryDays}</strong> ngày.
              </p>
              <p style="margin:0 0 20px;">
                <a href="{safeUrl}"
                   style="display:inline-block;background:#6366f1;color:#ffffff;text-decoration:none;padding:10px 18px;border-radius:8px;">
                  Tham gia workspace
                </a>
              </p>
              <p style="margin:0;font-size:12px;color:#64748b;word-break:break-all;">
                Hoặc mở liên kết: {safeUrl}
              </p>
            """;

        return (subject, text, HtmlTemplate.Replace("{BODY}", body));
    }

    /// <summary>
    /// "Quick email": a short notice a Manager composes (meeting, urgent item). The subject and body
    /// are the sender's own words, HTML-encoded and capped to the column limits.
    /// </summary>
    public static (string Subject, string TextBody, string HtmlBody) Quick(
        string workspaceName,
        string senderName,
        string subject,
        string body)
    {
        var safeWorkspace = Encode(workspaceName);
        var safeSender = Encode(senderName);

        // The body is plain text from the Manager: normalize line endings and turn the author's
        // newlines into <br /> FIRST, then HTML-encode. Encoding first would be wrong for the same
        // reason it is wrong for a link: the default encoder writes a newline as "&#xA;", and a
        // second pass would then escape our own <br /> markup.
        var encodedBody = EncodeMultiline(body);

        var text = $"""
            {subject}

            {body}

            — {senderName} (workspace "{workspaceName}")
            """;

        var htmlBody = $"""
              <h3 style="margin:0 0 12px;font-size:16px;">{Encode(subject)}</h3>
              <p style="margin:0 0 16px;line-height:1.6;">{encodedBody}</p>
              <p style="margin:0;font-size:13px;color:#475569;">
                — <strong>{safeSender}</strong> (workspace &quot;{safeWorkspace}&quot;)
              </p>
            """;

        return (Truncate(subject), text, HtmlTemplate.Replace("{BODY}", htmlBody));
    }

    /// <summary>
    /// Short, safe preview stored in <c>email_messages.body_preview</c> (≤ 500 chars).
    /// <b>Invitations pass only the workspace name — never the body</b>, because the body contains
    /// the raw accept token and a queryable column must not become a token leak.
    /// </summary>
    public static string InvitationPreview(string workspaceName)
        => Truncate($"Lời mời tham gia workspace \"{workspaceName}\".", 500);

    /// <summary>Preview for a quick email: the sender's own opening words, capped and flattened.</summary>
    public static string QuickPreview(string body)
    {
        var flat = body
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return flat.Length <= 500 ? flat : flat[..500];
    }

    /// <summary>
    /// HTML encoder restricted to <see cref="UnicodeRanges.All"/>: it escapes exactly the five
    /// structural characters (<c>&amp; &lt; &gt; " '</c>) and leaves Vietnamese diacritics as
    /// themselves. The default settings would also entity-encode every non-ASCII character, which
    /// keeps the message safe but makes the body unreadable to anyone inspecting raw MIME — and it
    /// would break the plain-text wording the invitation is verified against.
    /// </summary>
    private static readonly HtmlEncoder Encoder =
        HtmlEncoder.Create(UnicodeRanges.All);

    private static string Encode(string? value) => Encoder.Encode(value ?? string.Empty);

    /// <summary>
    /// Encodes a free-text body while keeping the author's paragraph breaks: each line is encoded
    /// on its own (so a literal newline is never turned into <c>&#xA;</c>) and the lines are then
    /// rejoined with <c>&lt;br /&gt;</c>.
    /// </summary>
    private static string EncodeMultiline(string? value)
    {
        var normalized = (value ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        return string.Join("<br />", normalized.Split('\n').Select(Encoder.Encode));
    }

    private static string Truncate(string value, int max = MaxSubjectLength)
        => value.Length <= max ? value : value[..max];
}
