using TeamNexus.Modules.Ai.Services.Email;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 11 §2.4 — the email renderers are pure functions, so their wording limits and (more
/// importantly) their escaping rules are verifiable without a database or an HTTP call.
/// <para>
/// The injection cases are the point of this file: a workspace name, a sender display name, a
/// subject and a body are all user input, and an unencoded value would render as markup in the
/// recipient's mail client.
/// </para>
/// </summary>
public sealed class EmailTemplateTests
{
    private const string AcceptUrl = "http://localhost:5173/invitations/accept?token=raw-token-value";

    // ---- invitation --------------------------------------------------------

    [Fact]
    public void Invitation_IncludesWorkspaceInviterRoleAndExpiry()
    {
        var (_, text, html) = EmailTemplates.Invitation(
            "Nhóm Phát Triển", "Nguyễn Văn A", "Member", AcceptUrl, 7);

        Assert.Contains("Nhóm Phát Triển", text);
        Assert.Contains("Nguyễn Văn A", text);
        Assert.Contains("Member", text);
        Assert.Contains("7", text);
        Assert.Contains("Nhóm Phát Triển", html);
    }

    [Fact]
    public void Invitation_ContainsTheAcceptUrlInBothBodies()
    {
        var (_, text, html) = EmailTemplates.Invitation("W", "A", "Member", AcceptUrl, 7);

        Assert.Contains(AcceptUrl, text);
        Assert.Contains(AcceptUrl, html);
    }

    [Fact]
    public void Invitation_SubjectStaysWithinTheColumnLimit()
    {
        var longName = new string('x', 400);

        var (subject, _, _) = EmailTemplates.Invitation(longName, "A", "Member", AcceptUrl, 7);

        Assert.True(subject.Length <= EmailTemplates.MaxSubjectLength, $"subject was {subject.Length}");
    }

    [Fact]
    public void Invitation_HtmlEncodesAWorkspaceNameThatLooksLikeMarkup()
    {
        var (_, _, html) = EmailTemplates.Invitation(
            "<script>alert(1)</script>", "A", "Member", AcceptUrl, 7);

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public void Invitation_HtmlEncodesTheInviterName()
    {
        var (_, _, html) = EmailTemplates.Invitation("W", "<img src=x onerror=alert(1)>", "Member", AcceptUrl, 7);

        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void Invitation_HtmlEncodesTheAcceptUrl()
    {
        // A quote in the URL must not be able to break out of the href attribute.
        var url = "http://localhost:5173/invitations/accept?token=a\"onmouseover=\"alert(1)";

        var (_, _, html) = EmailTemplates.Invitation("W", "A", "Member", url, 7);

        Assert.DoesNotContain("token=a\"onmouseover", html);
        Assert.Contains("&quot;", html);
    }

    [Fact]
    public void Invitation_WithAnEmptyWorkspaceName_DoesNotThrow()
    {
        var (subject, text, html) = EmailTemplates.Invitation("", "A", "Member", AcceptUrl, 7);

        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.False(string.IsNullOrWhiteSpace(html));
    }

    [Fact]
    public void InvitationPreview_ContainsTheWorkspaceNameButNeverTheToken()
    {
        var preview = EmailTemplates.InvitationPreview("Nhóm Phát Triển");

        Assert.Contains("Nhóm Phát Triển", preview);
        Assert.DoesNotContain("token", preview, StringComparison.OrdinalIgnoreCase);
    }

    // ---- quick email -------------------------------------------------------

    [Fact]
    public void Quick_IncludesSubjectSenderAndWorkspace()
    {
        var (subject, text, html) = EmailTemplates.Quick(
            "Nhóm Phát Triển", "Trần Thị B", "Họp nhóm 9h", "Mọi người có mặt đúng giờ nhé.");

        Assert.Equal("Họp nhóm 9h", subject);
        Assert.Contains("Trần Thị B", text);
        Assert.Contains("Nhóm Phát Triển", text);
        Assert.Contains("Mọi người có mặt đúng giờ nhé.", text);
        Assert.Contains("Họp nhóm 9h", html);
    }

    [Fact]
    public void Quick_HtmlEncodesTheSubjectAndBody()
    {
        var (_, _, html) = EmailTemplates.Quick(
            "W", "S", "<b>Tiêu đề</b>", "<img src=x onerror=alert(1)>");

        Assert.DoesNotContain("<b>Tiêu đề</b>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;b&gt;", html);
        Assert.Contains("&lt;img", html);
    }

    [Fact]
    public void Quick_PreservesTheAuthorsLineBreaksInHtml()
    {
        var (_, _, html) = EmailTemplates.Quick("W", "S", "Chủ đề", "dòng 1\ndòng 2");

        Assert.Contains("dòng 1", html);
        Assert.Contains("dòng 2", html);
        Assert.Contains("<br />", html);
    }

    [Fact]
    public void Quick_TruncatesTheSubjectToTheColumnLimit()
    {
        var longSubject = new string('y', 300);

        var (subject, _, _) = EmailTemplates.Quick("W", "S", longSubject, "body");

        Assert.Equal(EmailTemplates.MaxSubjectLength, subject.Length);
    }

    [Fact]
    public void QuickPreview_FlattensNewlinesAndStaysWithinTheColumnLimit()
    {
        var longBody = string.Join('\n', Enumerable.Repeat(new string('z', 100), 10));

        var preview = EmailTemplates.QuickPreview(longBody);

        Assert.DoesNotContain('\n', preview);
        Assert.True(preview.Length <= 500, $"preview was {preview.Length}");
    }

    [Fact]
    public void QuickPreview_OfAShortBody_IsTheBodyItself()
    {
        Assert.Equal("nội dung ngắn", EmailTemplates.QuickPreview("nội dung ngắn"));
    }
}
