using System.Text.Json;
using TeamNexus.Modules.Ai.Contracts;
using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Modules.Board.DTOs;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 14 §5 — the guardrails and token limits of every new feature (GUARD-1…GUARD-12).
/// <para>
/// The phase's fourth requirement is "giới hạn token áp dụng cho mọi tính năng mới". A guardrail that is
/// only exercised through a happy-path integration test is not really verified: these cases pin the
/// <b>clamping</b>, the <b>truncation priority</b> and the <b>re-derived proposal</b> directly, with no
/// database and no provider.
/// </para>
/// </summary>
public sealed class AiGuardrailTests
{
    // ---- GUARD-1 … GUARD-3: AiChatOptions clamping -------------------------

    [Fact]
    public void GUARD1_AiChatOptions_DefaultsMatchTheDocumentedBudget()
    {
        var effective = new AiChatOptions().Effective;

        // Giá trị mặc định là HỢP ĐỒNG với tài liệu bàn giao frontend: đổi chúng là đổi ngưỡng 400.
        Assert.Equal(12, effective.MaxHistoryMessages);
        Assert.Equal(8000, effective.MaxHistoryChars);
        Assert.Equal(1200, effective.MaxOutputTokens);
        Assert.Equal(0.3, effective.Temperature);
        Assert.Equal(10, effective.MaxCommentsInContext);
        Assert.Equal(500, effective.MaxCommentCharsInContext);
        Assert.Equal(6000, effective.MaxTaskContextChars);
        Assert.Equal(2000, effective.MaxAnswerChars);
    }

    [Theory]
    // Cấu hình vô lý KHÔNG được tạo ra một guardrail vô hiệu: mọi giá trị đều bị kẹp về dải an toàn.
    [InlineData(0, 2)]
    [InlineData(-100, 2)]
    [InlineData(1, 2)]
    [InlineData(12, 12)]
    [InlineData(999, 50)]
    public void GUARD2_MaxHistoryMessages_IsClampedSoTheGuardrailCannotBeSwitchedOff(int configured, int expected)
    {
        var options = new AiChatOptions { MaxHistoryMessages = configured };

        Assert.Equal(expected, options.Effective.MaxHistoryMessages);

        // `0` cấu hình sẽ khiến MỌI request bị 400 nếu không kẹp; `999` sẽ vô hiệu hoá chốt chặn chi phí.
        Assert.InRange(options.Effective.MaxHistoryMessages, 2, 50);
    }

    [Fact]
    public void GUARD3_MaxAnswerChars_CanNeverExceedTheCommentColumnLimit()
    {
        // `task_comments.content` bị giới hạn 2000; nếu guardrail này cao hơn, server sẽ nhận một bình
        // luận rồi bị cắt âm thầm ở tầng dữ liệu.
        Assert.Equal(2000, new AiChatOptions { MaxAnswerChars = 99999 }.Effective.MaxAnswerChars);
        Assert.Equal(1, new AiChatOptions { MaxAnswerChars = 0 }.Effective.MaxAnswerChars);
        Assert.Equal(500, new AiChatOptions { MaxAnswerChars = 500 }.Effective.MaxAnswerChars);
    }

    [Fact]
    public void GUARD3b_EveryClampKeepsAUsableRange()
    {
        var hostile = new AiChatOptions
        {
            MaxHistoryMessages = -5,
            MaxHistoryChars = -1,
            MaxOutputTokens = 0,
            Temperature = 99,
            MaxCommentsInContext = -3,
            MaxCommentCharsInContext = 0,
            MaxTaskContextChars = 1,
            MaxAnswerChars = int.MaxValue,
        }.Effective;

        Assert.InRange(hostile.MaxHistoryMessages, 2, 50);
        Assert.InRange(hostile.MaxHistoryChars, 500, 32_000);
        Assert.InRange(hostile.MaxOutputTokens, 64, 8192);
        Assert.InRange(hostile.Temperature, 0, 2);
        Assert.InRange(hostile.MaxCommentsInContext, 0, 50);
        Assert.InRange(hostile.MaxCommentCharsInContext, 50, 4000);
        Assert.InRange(hostile.MaxTaskContextChars, 500, 32_000);
        Assert.InRange(hostile.MaxAnswerChars, 1, 2000);
    }

    // ---- GUARD-4 … GUARD-5: ObserverOptions clamping ----------------------

    [Fact]
    public void GUARD4_AtRiskDeadline_DefaultsMatchTheRoadmapWording()
    {
        var thresholds = new ObserverOptions().ToThresholds();

        Assert.True(thresholds.AtRiskDeadlineEnabled);
        Assert.Equal(0.20, thresholds.AtRiskDeadlineRemainingRatio);
        Assert.Equal(2, thresholds.AtRiskDeadlineMinWindowDays);
        Assert.Equal(48, thresholds.AtRiskDeadlineStaleHours);
    }

    [Fact]
    public void GUARD5_AtRiskRatio_IsClampedAwayFromBothExtremesSoTheFeatureCannotBeDisabledBySetting()
    {
        // 0 ⇒ MỌI task mở đều "sắp hết hạn"; 1 ⇒ không task nào. Cả hai đều là cấu hình vô hiệu hoá tính
        // năng một cách âm thầm, nên bị kẹp về [0.05, 0.95].
        Assert.Equal(0.05, new ObserverOptions { AtRiskDeadlineRemainingRatio = 0 }.ToThresholds().AtRiskDeadlineRemainingRatio);
        Assert.Equal(0.95, new ObserverOptions { AtRiskDeadlineRemainingRatio = 1 }.ToThresholds().AtRiskDeadlineRemainingRatio);
        Assert.Equal(0.95, new ObserverOptions { AtRiskDeadlineRemainingRatio = 5 }.ToThresholds().AtRiskDeadlineRemainingRatio);
        Assert.Equal(0.5, new ObserverOptions { AtRiskDeadlineRemainingRatio = 0.5 }.ToThresholds().AtRiskDeadlineRemainingRatio);

        // Số ngày/giờ phải dương, nếu không "cửa sổ >= 0 ngày" và "48h" trở thành vô nghĩa.
        Assert.Equal(1, new ObserverOptions { AtRiskDeadlineMinWindowDays = 0 }.ToThresholds().AtRiskDeadlineMinWindowDays);
        Assert.Equal(1, new ObserverOptions { AtRiskDeadlineStaleHours = -10 }.ToThresholds().AtRiskDeadlineStaleHours);
    }

    // ---- GUARD-6 … GUARD-8: prompt truncation priority (P6) ---------------

    [Fact]
    public void GUARD6_WhenThePromptMustBeCut_TheStrongestSignalsSurviveAndTheWeakestAreDropped()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

        // 8 tín hiệu với severity trải đều; prompt bị ép xuống rất nhỏ để buộc phải cắt.
        var signals = new List<ObserverSignal>
        {
            Signal("AtRiskDeadline", ObserverSeverity.Medium, 3, now),
            Signal("Bottleneck", ObserverSeverity.Medium, 1, now),
            Signal("OverdueTask", ObserverSeverity.Critical, 40, now),
            Signal("StalledTask", ObserverSeverity.Low, 1, now),
            Signal("Overload", ObserverSeverity.High, 9, now),
            Signal("AtRiskDeadline", ObserverSeverity.Medium, 2, now),
            Signal("Bottleneck", ObserverSeverity.Low, 0, now),
            Signal("Overload", ObserverSeverity.Critical, 30, now),
        };

        var options = new ObserverOptions { MaxPromptCharacters = 1200 };
        var snapshot = new ObserverWorkspaceSnapshot(Guid.NewGuid(), [], [], now);

        var payload = ObserverSummarizer.BuildPayload(
            snapshot,
            new ObserverSignalSet(signals, TruncatedSignals: 0),
            options,
            out var truncatedByPrompt);

        using var document = JsonDocument.Parse(payload);
        var kept = document.RootElement.GetProperty("signals").EnumerateArray()
            .Select(s => (
                Type: s.GetProperty("type").GetString()!,
                Severity: s.GetProperty("severity").GetString()!,
                Weight: s.GetProperty("weight").GetInt32()))
            .ToList();

        // Cắt bớt thật sự xảy ra, và được BÁO CÁO (P4) chứ không âm thầm.
        Assert.True(truncatedByPrompt > 0, "Case này phải buộc phải cắt tín hiệu.");
        Assert.Equal(signals.Count - kept.Count, truncatedByPrompt);
        Assert.InRange(kept.Count, 1, signals.Count - 1);

        // Những tín hiệu MẠNH NHẤT phải sống sót; những tín hiệu yếu nhất phải bị bỏ.
        Assert.Contains(kept, s => s.Severity == ObserverSeverity.Critical);

        // Và danh sách giữ lại vẫn theo thứ tự severity giảm dần (bất biến của payload).
        for (var i = 1; i < kept.Count; i++)
        {
            Assert.True(
                NotificationSeverities.Rank(kept[i - 1].Severity) >= NotificationSeverities.Rank(kept[i].Severity),
                "Payload phải giữ thứ tự severity giảm dần.");
        }

        // Tổng độ dài vẫn nằm trong ngân sách đã cấu hình.
        Assert.True(payload.Length <= options.MaxPromptCharacters);
    }

    [Fact]
    public void GUARD7_ATinyPromptBudgetKeepsTheMostSevereSignal_NotTheFirstOneHandedOver()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

        // Cố tình đưa tín hiệu YẾU lên ĐẦU: thứ tự đầu vào không được quyết định thứ tự sống sót.
        var signals = new List<ObserverSignal>
        {
            Signal("Bottleneck", ObserverSeverity.Low, 0, now),
            Signal("StalledTask", ObserverSeverity.Low, 1, now),
            Signal("OverdueTask", ObserverSeverity.Critical, 40, now),
        };

        var options = new ObserverOptions { MaxPromptCharacters = 1100 };
        var snapshot = new ObserverWorkspaceSnapshot(Guid.NewGuid(), [], [], now);

        var payload = ObserverSummarizer.BuildPayload(
            snapshot, new ObserverSignalSet(signals, 0), options, out _);

        using var document = JsonDocument.Parse(payload);
        var severities = document.RootElement.GetProperty("signals").EnumerateArray()
            .Select(s => s.GetProperty("severity").GetString())
            .ToList();

        // Tín hiệu Critical vẫn phải có mặt ngay cả khi ngân sách chỉ đủ cho một vài tín hiệu.
        Assert.Contains(ObserverSeverity.Critical, severities);
    }

    [Fact]
    public void GUARD8_APayloadThatFitsIsNeverTruncated()
    {
        var now = new DateTimeOffset(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);
        var signals = new List<ObserverSignal>
        {
            Signal("OverdueTask", ObserverSeverity.High, 5, now),
            Signal("AtRiskDeadline", ObserverSeverity.Medium, 2, now),
        };

        var options = new ObserverOptions();
        var snapshot = new ObserverWorkspaceSnapshot(Guid.NewGuid(), [], [], now);

        ObserverSummarizer.BuildPayload(
            snapshot, new ObserverSignalSet(signals, 0), options, out var truncatedByPrompt);

        Assert.Equal(0, truncatedByPrompt);
    }

    // ---- GUARD-9 … GUARD-12: the board template's own bounds --------------

    [Fact]
    public void GUARD9_ColumnAndTaskLimitsAreTheDocumentedProductRules()
    {
        Assert.Equal(2, BoardTemplatePrompts.MinColumns);
        Assert.Equal(6, BoardTemplatePrompts.MaxColumns);
        Assert.Equal(5, BoardTemplatePrompts.MinTasks);
        Assert.Equal(10, BoardTemplatePrompts.MaxTasks);

        // Prompt phải NÓI RA đúng những con số này, nếu không model không thể tuân thủ.
        var systemPrompt = BoardTemplatePrompts.BuildSystemPrompt();

        Assert.Contains("6", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("10", systemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void GUARD10_MoreColumnsThanAllowed_AreDroppedAndExactlyOneDoneColumnRemains()
    {
        var columns = Enumerable.Range(1, BoardTemplatePrompts.MaxColumns + 4)
            .Select(i => new AiBoardTemplateColumn { Name = $"Cột {i}", IsDone = false })
            .ToList();

        var normalized = BoardTemplateValidator.NormalizeColumns(columns);

        Assert.Equal(BoardTemplatePrompts.MaxColumns, normalized.Count);
        Assert.Single(normalized, c => c.IsDone);

        // Cột bị bỏ là những cột CUỐI (thứ tự là luồng công việc, không được xáo trộn).
        Assert.Equal("Cột 1", normalized[0].Name);
        Assert.Equal($"Cột {BoardTemplatePrompts.MaxColumns}", normalized[^1].Name);
    }

    [Fact]
    public void GUARD11_TaskCountIsCappedByTheSmallerOfTheProductAndOperatorLimits()
    {
        var output = new AiBoardTemplateOutput
        {
            BoardName = "Bảng giới hạn",
            Columns =
            [
                new AiBoardTemplateColumn { Name = "Cần làm", IsDone = false },
                new AiBoardTemplateColumn { Name = "Xong", IsDone = true },
            ],
            // Nhiều hơn cả trần của sản phẩm (10) lẫn trần của vận hành.
            Tasks = Enumerable.Range(1, 25)
                .Select(i => new AiBoardTemplateTask { Title = $"Việc {i:D2}", ColumnName = "Cần làm" })
                .ToList(),
        };

        var baseline = BoardTemplateValidator.Normalize(
            output, "Mô tả dự án", [], [], maxTaskCount: 20);
        Assert.Equal(BoardTemplatePrompts.MaxTasks, baseline.Tasks.Count);

        // Trần của VẬN HÀNH nhỏ hơn ⇒ nó thắng (đúng luật "giá trị nhỏ hơn thắng").
        var stricter = BoardTemplateValidator.Normalize(
            output, "Mô tả dự án", [], [], maxTaskCount: 6);
        Assert.Equal(6, stricter.Tasks.Count);

        // Và 0/âm nghĩa là "không cấu hình" ⇒ dùng trần sản phẩm, KHÔNG phải "không có task nào".
        var unset = BoardTemplateValidator.Normalize(
            output, "Mô tả dự án", [], [], maxTaskCount: 0);
        Assert.Equal(BoardTemplatePrompts.MaxTasks, unset.Tasks.Count);
    }

    [Fact]
    public void GUARD12_EveryProposalEndsUpWithExactlyOneDoneColumnAndUsableTaskColumns()
    {
        // Quét toàn bộ tổ hợp "cột nào được AI đánh dấu done" (0, 1 hoặc nhiều) — bất biến phải luôn đúng.
        for (var mask = 0; mask < 8; mask++)
        {
            var columns = Enumerable.Range(0, 3)
                .Select(i => new AiBoardTemplateColumn
                {
                    Name = $"Cột {i}",
                    IsDone = (mask & (1 << i)) != 0,
                })
                .ToList();

            var normalized = BoardTemplateValidator.NormalizeColumns(columns);

            Assert.Single(normalized, c => c.IsDone);

            // Ai cũng phải tìm được một cột để đặt task: `columnName` hợp lệ hoặc cột đầu không-done.
            var tasks = BoardTemplateValidator.NormalizeTasks(
                [new AiBoardTemplateTask { Title = "Việc", ColumnName = "Không tồn tại" }],
                normalized,
                [],
                [],
                maxTasks: 5);

            var task = Assert.Single(tasks);
            Assert.Contains(task.ColumnName, normalized.Select(c => c.Name));
            Assert.False(normalized.Single(c => c.Name == task.ColumnName).IsDone);
        }
    }

    [Fact]
    public void GUARD12b_ANamelessBoardStillGetsAUsableName()
    {
        // `boards.name` là NOT NULL: đề xuất không tên phải được suy ra tên, không được để rỗng.
        var derived = BoardTemplateValidator.NormalizeBoardName(null, "Xây dựng hệ thống báo cáo tự động");

        Assert.False(string.IsNullOrWhiteSpace(derived));
        Assert.Contains("Xây dựng hệ thống báo cáo", derived, StringComparison.Ordinal);

        var empty = BoardTemplateValidator.NormalizeBoardName("   ", "  ");
        Assert.False(string.IsNullOrWhiteSpace(empty));

        // Tên quá dài bị CẮT, không bị bỏ (một cái tên dài vẫn hơn một bảng không tên).
        var longName = BoardTemplateValidator.NormalizeBoardName(new string('x', 300), "mô tả");
        Assert.Equal(BoardTemplatePrompts.MaxBoardNameLength, longName.Length);
    }

    // ---- helpers ----------------------------------------------------------

    private static ObserverSignal Signal(
        string type, string severity, int weight, DateTimeOffset now)
        => new(
            type,
            severity,
            weight,
            $"{type} mẫu cho kiểm thử cắt bớt prompt với trọng số {weight}.",
            [Guid.NewGuid()],
            [Guid.NewGuid()]);
}
