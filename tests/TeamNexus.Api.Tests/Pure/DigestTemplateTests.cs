using TeamNexus.Modules.Ai.Services.Email;
using TeamNexus.Modules.Board.DTOs;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 13 §3.3 — <see cref="EmailTemplates.DailyDigest"/> (DT-1…DT-8).
/// <para>
/// Pure by construction: the template takes a fully assembled <see cref="DailyDigestContent"/> and a date,
/// so no database, no mail transport and no clock are involved. That is what makes the escaping rules
/// affordable to assert exactly — and escaping is the point of this suite, because every value in a digest
/// (workspace name, board name, task title, display name) is text a user typed.
/// </para>
/// </summary>
public sealed class DigestTemplateTests
{
    private static readonly DateOnly SendDate = new(2026, 4, 7);

    // ---- DT-1: kind + subject -------------------------------------------------

    [Fact]
    public void DT1_KindIsStableAndTheSubjectCarriesTheLocalDate()
    {
        // The literal is what `email_messages.kind` stores and what the dedupe query filters on, so a
        // rename here would silently start sending a second digest per day.
        Assert.Equal("DailyDigest", EmailKinds.DailyDigest);

        var (subject, _, _) = EmailTemplates.DailyDigest(Content());

        Assert.True(subject.Length <= EmailTemplates.MaxSubjectLength);
        Assert.Contains("07/04/2026", subject);
        Assert.Contains("TeamNexus", subject);
    }

    // ---- DT-2 / DT-6: plain-text body -----------------------------------------

    [Fact]
    public void DT2_TextBodyListsWorkspacesAndTasksAndCarriesBothLinks()
    {
        var (_, text, _) = EmailTemplates.DailyDigest(Content());

        Assert.Contains("Workspace Alpha", text);
        Assert.Contains("Sửa lỗi đăng nhập", text);
        Assert.Contains("https://app.example.test/workspaces/11111111-1111-1111-1111-111111111111/dashboard", text);
        Assert.Contains("https://app.example.test/profile#notifications", text);
    }

    [Fact]
    public void DT6_TextBodyContainsNoHtmlMarkup()
    {
        var (_, text, _) = EmailTemplates.DailyDigest(Content());

        // A plain-text reader must not be shown tags, and `<` only ever appears here if someone forgot to
        // encode — which would also be an injection in the HTML part.
        Assert.DoesNotContain("<", text);
    }

    // ---- DT-3: escaping -------------------------------------------------------

    [Fact]
    public void DT3_HtmlBodyEscapesEveryInterpolatedValue()
    {
        var content = Content(title: "<script>alert(1)</script>", workspaceName: "<img src=x onerror=alert(1)>");

        var (_, text, html) = EmailTemplates.DailyDigest(content);

        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img src=x", html);
        Assert.Contains("&lt;script&gt;", html);

        // The escaping is a rendering concern, so the plain-text body stays readable as typed.
        Assert.Contains("<script>alert(1)</script>", text);
    }

    // ---- DT-4: Vietnamese diacritics survive ----------------------------------

    [Fact]
    public void DT4_VietnameseDiacriticsArePreserved()
    {
        var content = Content(workspaceName: "Báo cáo Quý", displayName: "Nguyễn Văn A", title: "Kiểm thử");

        var (_, text, html) = EmailTemplates.DailyDigest(content);

        // Phase 11 §P5: the restricted HtmlEncoder escapes the five structural characters and leaves
        // non-ASCII alone. The default settings would entity-encode every Vietnamese letter.
        Assert.Contains("Nguyễn Văn A", text);
        Assert.Contains("Báo cáo Quý", html);
        Assert.Contains("Nguyễn Văn A", html);
        Assert.DoesNotContain("&#x", html);
    }

    // ---- DT-5: never throws on sparse input ----------------------------------

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void DT5_SparseContentNeverThrows(bool emptyWorkspaces, bool emptyBuckets)
    {
        var content = Content(
            workspaces: emptyWorkspaces ? [] : [Section(emptyBuckets ? null : Task())],
            displayName: string.Empty);

        var (subject, text, html) = EmailTemplates.DailyDigest(content);

        Assert.False(string.IsNullOrWhiteSpace(subject));
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.False(string.IsNullOrWhiteSpace(html));
    }

    // ---- DT-7: unsubscribe link ----------------------------------------------

    [Fact]
    public void DT7_TheUnsubscribeLinkAppearsInBothBodies()
    {
        var (_, text, html) = EmailTemplates.DailyDigest(Content());

        Assert.Contains("Tắt nhận", text);
        Assert.Contains("Tắt nhận", html);
        Assert.Contains("#notifications", text);
        Assert.Contains("#notifications", html);
    }

    // ---- DT-8: optional fields are omitted, never printed as "null" ----------

    [Fact]
    public void DT8_MissingOptionalFieldsAreOmittedRatherThanPrinted()
    {
        var withDays = Content(overdueByDays: 3, boardName: "Board chính", priority: "High");

        // Nothing optional at all: no lateness, no due date, no priority, no board.
        var (_, bareText, _) = EmailTemplates.DailyDigest(
            Content(overdueByDays: null, omitDueDate: true, priority: null, boardName: null));

        // The per-task context is the parenthetical; "Quá hạn" also appears as the bucket heading, so the
        // assertions are about the row, never about the heading.
        var (_, lateText, _) = EmailTemplates.DailyDigest(withDays);
        Assert.Contains("(Board chính · Quá hạn 3 ngày · High)", lateText);

        Assert.DoesNotContain("(Quá hạn", bareText);
        Assert.DoesNotContain("(Hạn ", bareText);
        Assert.DoesNotContain("null", bareText);
        Assert.DoesNotContain(" · ", bareText);
    }

    [Fact]
    public void DT8b_DueDateWithoutLatenessShowsTheDateNotTheWordNull()
    {
        // A task that is due but NOT yet late: the per-task label is the due date, and it must not claim
        // lateness (the bucket heading legitimately still says "Quá hạn").
        var (_, text, _) = EmailTemplates.DailyDigest(Content(overdueByDays: null));

        Assert.Contains("(Board chính · Hạn 07/04 · High)", text);
        Assert.DoesNotContain("Quá hạn 1 ngày", text);
        Assert.DoesNotContain("null", text);
    }

    [Fact]
    public void DailyDigestPreview_StaysWithinTheColumnLimitAndLeaksNoTaskTitle()
    {
        var preview = EmailTemplates.DailyDigestPreview(workspaceCount: 3, sendDate: SendDate);

        Assert.True(preview.Length <= 500);
        Assert.Contains("07/04/2026", preview);
        Assert.Contains("3", preview);

        // email_messages is a queryable column: it gets a summary, not a copy of the mail.
        Assert.DoesNotContain("Sửa lỗi", preview);
    }

    // ---- fixtures -------------------------------------------------------------

    private static DailyDigestContent Content(
        string workspaceName = "Workspace Alpha",
        string displayName = "Trần An",
        string title = "Sửa lỗi đăng nhập",
        string? boardName = "Board chính",
        DateTimeOffset? dueDate = null,
        int? overdueByDays = 1,
        string? priority = "High",
        bool omitDueDate = false,
        IReadOnlyList<DailyDigestWorkspaceSection>? workspaces = null)
    {
        var section = Section(
            Task(
                title: title,
                boardName: boardName,
                // The default is a real due date so the common cases render one; `omitDueDate` is how a
                // caller asks for "no due date at all" and proves nothing prints the word "null".
                dueDate: omitDueDate ? null : dueDate ?? new DateTimeOffset(2026, 4, 7, 0, 0, 0, TimeSpan.Zero),
                overdueByDays: overdueByDays,
                priority: priority),
            workspaceName: workspaceName);

        return new DailyDigestContent(
            "reader@example.test",
            displayName,
            SendDate,
            workspaces ?? [section],
            "https://app.example.test/workspaces/11111111-1111-1111-1111-111111111111/dashboard",
            "https://app.example.test/profile#notifications");
    }

    /// <summary>One dashboard task row with only the fields a digest renders.</summary>
    private static DashboardTaskItem Task(
        string title = "Sửa lỗi đăng nhập",
        string? boardName = "Board chính",
        DateTimeOffset? dueDate = null,
        int? overdueByDays = 1,
        string? priority = "High")
        => new(
            Guid.NewGuid(),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.NewGuid(),
            title,
            boardName ?? string.Empty,
            "Todo",
            // `null` here means "no due date at all"; the callers that want a date pass one or use the
            // default below via `Content`.
            dueDate,
            priority,
            new DateTimeOffset(2026, 4, 1, 0, 0, 0, TimeSpan.Zero),
            Guid.NewGuid(),
            false,
            overdueByDays);

    /// <summary>A workspace section whose <c>overdue</c> bucket holds the given rows.</summary>
    private static DailyDigestWorkspaceSection Section(
        DashboardTaskItem? task = null,
        string workspaceName = "Workspace Alpha",
        IReadOnlyList<DashboardTaskItem>? items = null)
    {
        var listed = items ?? (task is null ? [] : new[] { task });
        var bucket = new DashboardTaskBucket(listed.Count, listed);

        return new DailyDigestWorkspaceSection(new DashboardResponse(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            workspaceName,
            new DateTimeOffset(2026, 4, 7, 1, 0, 0, TimeSpan.Zero),
            3,
            new DashboardMyTasks(bucket, new DashboardTaskBucket(0, []), new DashboardTaskBucket(0, [])),
            [],
            false,
            [],
            new DashboardSummary(listed.Count, 0, listed.Count, listed.Count, listed.Count)));
    }
}
