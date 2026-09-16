using System.Net;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;

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

    /// <summary>
    /// The scheduled "your work today" digest (Phase 13 ô C). One row per recipient per day, which is also
    /// what makes the send idempotent — see <c>DailyDigestRunner</c>.
    /// </summary>
    public const string DailyDigest = "DailyDigest";
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
                Nếu có thắc mắc về lời mời này, bạn có thể phản hồi trực tiếp email hoặc liên hệ với quản trị viên.
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

    /// <summary>
    /// The daily digest: what is overdue, what is due soon and what was assigned recently, per workspace
    /// (Phase 13 §3.3).
    /// <para>
    /// <b>Pure.</b> No clock (the date arrives as <c>content.SendDateLocal</c>), no EF, no I/O — so the
    /// wording, the escaping and the bucketing can be verified by a plain unit test, exactly like
    /// <see cref="Invitation"/> and <see cref="Quick"/>. That matters more here than anywhere else,
    /// because every value in this mail is user-controlled (task titles, board names, workspace names) and
    /// a task titled <c>&lt;script&gt;</c> would otherwise render as markup in the recipient's client.
    /// </para>
    /// <para>
    /// The plain-text body is a real alternative, not a stripped copy: readers of a text-only client get
    /// the same information and both call-to-action links.
    /// </para>
    /// </summary>
    public static (string Subject, string TextBody, string HtmlBody) DailyDigest(DailyDigestContent content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var dateLabel = content.SendDateLocal.ToString(
            "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);

        var safeName = Encode(content.RecipientDisplayName);
        var safeDashboardUrl = Encode(content.DashboardUrl);
        var safeUnsubscribeUrl = Encode(content.UnsubscribeUrl);

        var subject = Truncate($"TeamNexus — việc của bạn hôm {dateLabel}");

        var text = new System.Text.StringBuilder();
        text.AppendLine($"Xin chào {content.RecipientDisplayName},".TrimEnd());
        text.AppendLine();
        text.AppendLine($"Đây là tóm tắt công việc của bạn ngày {dateLabel}.");
        text.AppendLine();

        foreach (var section in content.Workspaces)
        {
            text.AppendLine($"== {section.Dashboard.WorkspaceName} ==");

            foreach (var bucket in Buckets(section))
            {
                if (bucket.Items.Count == 0)
                {
                    continue;
                }

                text.AppendLine($"{bucket.Title} ({bucket.Total}):");

                foreach (var item in bucket.Items)
                {
                    text.AppendLine($"  - {item.Title}{context(item)}");
                }

                if (bucket.Total > bucket.Items.Count)
                {
                    text.AppendLine($"  … và {bucket.Total - bucket.Items.Count} thẻ khác");
                }

                text.AppendLine();
            }
        }

        text.AppendLine($"Mở bảng điều khiển: {content.DashboardUrl}");
        text.AppendLine($"Tắt nhận email tóm tắt: {content.UnsubscribeUrl}");

        var html = new System.Text.StringBuilder();
        html.AppendLine($"""
              <h3 style="margin:0 0 12px;font-size:16px;">Việc của bạn hôm {dateLabel}</h3>
              <p style="margin:0 0 16px;line-height:1.6;">Xin chào <strong>{safeName}</strong>,</p>
            """);

        foreach (var section in content.Workspaces)
        {
            html.AppendLine($"""
              <h4 style="margin:16px 0 8px;font-size:14px;color:#4338ca;">
                {Encode(section.Dashboard.WorkspaceName)}
              </h4>
              <table style="width:100%;border-collapse:collapse;font-size:13px;">
            """);

            foreach (var bucket in Buckets(section))
            {
                if (bucket.Items.Count == 0)
                {
                    continue;
                }

                var rows = new System.Text.StringBuilder();
                foreach (var item in bucket.Items)
                {
                    rows.AppendLine($"""
                    <tr>
                      <td style="padding:4px 0;border-bottom:1px solid #e2e8f0;">
                        {Encode(item.Title)}
                        <span style="color:#64748b;">{Encode(context(item))}</span>
                      </td>
                    </tr>
                    """);
                }

                var more = bucket.Total > bucket.Items.Count
                    ? $"<p style=\"margin:4px 0 0;font-size:12px;color:#64748b;\">… và {bucket.Total - bucket.Items.Count} thẻ khác</p>"
                    : string.Empty;

                html.AppendLine($"""
                  <tr>
                    <td style="padding:8px 0 2px;font-weight:600;">
                      {Encode(bucket.Title)} ({bucket.Total})
                    </td>
                  </tr>
                  {rows}{more}
                """);
            }

            html.AppendLine("</table>");
        }

        html.AppendLine($"""
              <p style="margin:20px 0 8px;">
                <a href="{safeDashboardUrl}"
                   style="display:inline-block;background:#6366f1;color:#ffffff;text-decoration:none;padding:10px 18px;border-radius:8px;">
                  Mở bảng điều khiển
                </a>
              </p>
              <p style="margin:0;font-size:12px;color:#64748b;">
                Không muốn nhận email này nữa?
                <a href="{safeUnsubscribeUrl}" style="color:#6366f1;">Tắt nhận email tóm tắt</a>.
              </p>
            """);

        return (subject, text.ToString().TrimEnd(), HtmlTemplate.Replace("{BODY}", html.ToString()));
    }

    /// <summary>
    /// Short preview for the digest audit row: the date plus how many workspaces it covers. The task
    /// titles stay out of it — <c>email_messages</c> is a queryable column and already holds a preview,
    /// not a second copy of the mail.
    /// </summary>
    public static string DailyDigestPreview(int workspaceCount, DateOnly sendDate)
        => Truncate(
            $"Tóm tắt công việc ngày {sendDate:dd/MM/yyyy} ({workspaceCount} workspace).",
            500);

    /// <summary>One rendered section of the digest: a heading, the exact total and the listed tasks.</summary>
    private sealed record DigestBucket(string Title, int Total, IReadOnlyList<DashboardTaskItem> Items);

    /// <summary>
    /// Projects a workspace's dashboard into the three buckets the mail shows, in urgency order.
    /// <para>
    /// Reuses <c>DashboardResponse.MyTasks</c> verbatim — including Phase 12's deliberate rule that
    /// <c>dueSoon</c> excludes <c>overdue</c> — so the e-mail and the web page can never disagree about
    /// which task is in which bucket.
    /// </para>
    /// </summary>
    private static IEnumerable<DigestBucket> Buckets(DailyDigestWorkspaceSection section)
    {
        var myTasks = section.Dashboard.MyTasks;

        yield return new DigestBucket("Quá hạn", myTasks.Overdue.Count, myTasks.Overdue.Items);
        yield return new DigestBucket("Sắp đến hạn", myTasks.DueSoon.Count, myTasks.DueSoon.Items);
        yield return new DigestBucket("Mới được giao", myTasks.RecentlyAssigned.Count, myTasks.RecentlyAssigned.Items);
    }

    /// <summary>
    /// The parenthetical after a task title: board, due date, priority and lateness — only the parts that
    /// exist, so a task with no due date never prints "null".
    /// </summary>
    private static string context(DashboardTaskItem item)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(item.BoardName))
        {
            parts.Add(item.BoardName);
        }

        if (item.OverdueByDays is { } days)
        {
            parts.Add($"Quá hạn {days} ngày");
        }
        else if (item.DueDate is { } due)
        {
            parts.Add($"Hạn {due.UtcDateTime:dd/MM}");
        }

        if (!string.IsNullOrWhiteSpace(item.Priority))
        {
            parts.Add(item.Priority!);
        }

        return parts.Count == 0 ? string.Empty : $" ({string.Join(" · ", parts)})";
    }

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
