using TeamNexus.Modules.Ai.Options;
using TeamNexus.Modules.Ai.Services;
using TeamNexus.Shared.Risk;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 14 §3.1 — <c>AtRiskDeadline</c> (RISK-1…RISK-14).
/// <para>
/// Pure by construction: hand-built <see cref="ObserverTaskSnapshot"/> rows plus the shared
/// <see cref="DeadlineRiskRules"/>, no database, no HTTP, no clock. Every awkward case is therefore
/// affordable to assert <b>exactly</b> — the strict 20% edge, the two-day window floor, the 48-hour
/// stale gate, a comment that "revives" a task — instead of settling for the comfortable middle of a
/// range.
/// </para>
/// </summary>
public sealed class ObserverRiskTests
{
    /// <summary>Frozen "now". Far enough from real time that a wall-clock leak would be obvious.</summary>
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Default thresholds, i.e. what <c>ObserverOptions</c> produces with nothing configured.</summary>
    private static readonly ObserverThresholds Defaults = new ObserverOptions().ToThresholds();

    // ---- RISK-1 / RISK-2: the 20% edge and the window floor ---------------

    [Fact]
    public void RISK1_TheTwentyPercentEdgeIsStrict_ExactlyAtTheEdgeIsNotAtRisk()
    {
        // Cửa sổ 10 ngày, hạn còn ĐÚNG 2.0 ngày = 20% ⇒ CHƯA tính là rủi ro (biên dùng `<`, không `<=`).
        var exactlyAtEdge = Task(
            createdDaysAgo: 8,
            dueInDays: 2.0,
            lastActivityHoursAgo: 49);

        Assert.False(DeadlineRiskRules.IsAtRiskDeadline(WorkItem(exactlyAtEdge), Now));
        Assert.Empty(ObserverRisk.Detect([exactlyAtEdge], Now, Defaults));

        // Nhích vào trong một chút (1.9 ngày = 19%) VÀ đã 49 h không ai động tới ⇒ RỦI RO.
        var justInside = Task(
            createdDaysAgo: 8.1,
            dueInDays: 1.9,
            lastActivityHoursAgo: 49);

        var detected = ObserverRisk.Detect([justInside], Now, Defaults);

        Assert.Single(detected);
        Assert.Equal(justInside.TaskId, detected[0].TaskId);
    }

    [Fact]
    public void RISK2_ShortWindowTasksAreNeverAtRisk_EvenAtFivePercentRemaining()
    {
        // Task 36 h (dưới sàn 2 ngày): còn 5% thời gian vẫn KHÔNG báo. Không có sàn này thì mọi task
        // ngắn hạn (tạo 10:00, hạn 22:00 cùng ngày) sẽ bắn cảnh báo giả ở mốc 2 h24.
        var shortWindow = Task(
            createdDaysAgo: 1.5 - 0.075, // 36 h trước hạn
            dueInDays: 0.075,            // còn 5% của 36 h ≈ 1 h48
            lastActivityHoursAgo: 49);

        Assert.Equal(
            DeadlineRisk.None,
            DeadlineRiskRules.Classify(WorkItem(shortWindow), Now, minWindowDays: 2));
        Assert.Empty(ObserverRisk.Detect([shortWindow], Now, Defaults));

        // Cùng mốc "còn 5%" nhưng cửa sổ 3 ngày ⇒ ĐỦ điều kiện sàn ⇒ báo bình thường.
        var longEnough = Task(createdDaysAgo: 3 - 0.15, dueInDays: 0.15, lastActivityHoursAgo: 49);

        Assert.Single(ObserverRisk.Detect([longEnough], Now, Defaults));
    }

    // ---- RISK-3 / RISK-4: the stale gate and "movement revives" ------------

    [Fact]
    public void RISK3_TouchedWithinFortyEightHours_IsNotReported()
    {
        // Đã vào vùng "còn < 20%" nhưng mới có người động vào 47 h trước ⇒ KHÔNG báo.
        var touchedRecently = Task(createdDaysAgo: 9.5, dueInDays: 0.4, lastActivityHoursAgo: 47);
        Assert.Empty(ObserverRisk.Detect([touchedRecently], Now, Defaults));

        // 49 h ⇒ đã quá ngưỡng bỏ rơi ⇒ báo.
        var abandoned = Task(createdDaysAgo: 9.5, dueInDays: 0.4, lastActivityHoursAgo: 49);
        Assert.Single(ObserverRisk.Detect([abandoned], Now, Defaults));

        // Đúng 48 h ⇒ biên dùng `<=` cho phần "còn hoạt động" ⇒ CHƯA báo (nhất quán với RISK-1).
        var exactlyFortyEight = Task(createdDaysAgo: 9.5, dueInDays: 0.4, lastActivityHoursAgo: 48);
        Assert.Empty(ObserverRisk.Detect([exactlyFortyEight], Now, Defaults));
    }

    [Fact]
    public void RISK4_ACommentNewerThanTheLastEdit_RevivesTheTask()
    {
        // `updated_at` cũ (72 h) nhưng có comment 2 h trước ⇒ task ĐANG được bàn ⇒ không bỏ rơi.
        var commented = Task(
            createdDaysAgo: 9.5,
            dueInDays: 0.4,
            lastActivityHoursAgo: 72,
            lastCommentHoursAgo: 2);

        Assert.Empty(ObserverRisk.Detect([commented], Now, Defaults));

        // Đối chứng: bỏ comment đi thì cùng task đó BỊ báo ⇒ chứng minh comment là thứ "đánh thức".
        var withoutComment = Task(createdDaysAgo: 9.5, dueInDays: 0.4, lastActivityHoursAgo: 72);
        Assert.Single(ObserverRisk.Detect([withoutComment], Now, Defaults));
    }

    // ---- RISK-5 / RISK-6 / RISK-10 / RISK-11: the exclusions --------------

    [Fact]
    public void RISK5_AlreadyOverdueTasksBelongToOverdueTask_NotToAtRiskDeadline()
    {
        // `remaining <= 0` ⇒ đó là việc của OverdueTask. Nếu detector này cũng báo thì mỗi task trễ
        // hạn sẽ sinh HAI cảnh báo cho cùng một vấn đề.
        var overdue = Task(createdDaysAgo: 10, dueInDays: -0.5, lastActivityHoursAgo: 72);

        Assert.Equal(DeadlineRisk.None, DeadlineRiskRules.Classify(WorkItem(overdue), Now));
        Assert.Empty(ObserverRisk.Detect([overdue], Now, Defaults));

        // Và một signal set thật cũng chỉ có OverdueTask, không có AtRiskDeadline.
        var signals = ObserverSignalDetector.Analyze(
            new ObserverWorkspaceSnapshot(
                Guid.NewGuid(),
                [overdue],
                [new ObserverColumnSnapshot(overdue.ColumnId, overdue.BoardId, "Todo", IsDone: false)],
                Now),
            Defaults);

        Assert.Contains(signals.Signals, s => s.Type == NotificationTypes.OverdueTask);
        Assert.DoesNotContain(signals.Signals, s => s.Type == NotificationTypes.AtRiskDeadline);
    }

    [Fact]
    public void RISK6_DisabledSwitchProducesNoSignalAtAll()
    {
        var risky = Task(createdDaysAgo: 9.5, dueInDays: 0.4, lastActivityHoursAgo: 72);

        var disabled = Defaults with { AtRiskDeadlineEnabled = false };
        Assert.Empty(ObserverRisk.Detect([risky], Now, disabled));

        // Cùng task, cùng thời điểm, chỉ khác cái cờ ⇒ báo bình thường.
        Assert.Single(ObserverRisk.Detect([risky], Now, Defaults));
    }

    [Fact]
    public void RISK10_TasksInADoneColumnAreNeverCandidates()
    {
        var done = Task(createdDaysAgo: 9.5, dueInDays: 0.4, lastActivityHoursAgo: 72, isDone: true);

        // Detector loại task ở cột done MỘT LẦN cho mọi detector ⇒ signal set sạch.
        var signals = ObserverSignalDetector.Analyze(
            new ObserverWorkspaceSnapshot(
                WorkspaceId,
                [done],
                [new ObserverColumnSnapshot(done.ColumnId, done.BoardId, "Done", IsDone: true)],
                Now),
            Defaults);

        Assert.Empty(signals.Signals);
    }

    [Fact]
    public void RISK11_TasksWithoutADueDateAreNeverCandidates()
    {
        var noDue = Task(createdDaysAgo: 30, dueInDays: null, lastActivityHoursAgo: 720);

        Assert.Equal(DeadlineRisk.None, DeadlineRiskRules.Classify(WorkItem(noDue), Now));
        Assert.Empty(ObserverRisk.Detect([noDue], Now, Defaults));
    }

    // ---- RISK-7 / RISK-8 / RISK-9: severity, weight, ordering -------------

    [Theory]
    // (giờ còn lại, severity mong đợi) — biên 24 h và 72 h. Cửa sổ 35 ngày đủ rộng để 120 h vẫn
    // nằm trong vùng "còn < 20%" (120 h / 35 ngày ≈ 14%), nhờ vậy thang severity được kiểm tra độc lập
    // với ngưỡng tỉ lệ.
    [InlineData(10, "Critical")]
    [InlineData(24, "Critical")]   // đúng 24 h vẫn là Critical
    [InlineData(25, "High")]
    [InlineData(72, "High")]       // đúng 72 h vẫn là High
    [InlineData(73, "Medium")]
    [InlineData(120, "Medium")]
    public void RISK7_SeverityEscalatesAtTwentyFourAndSeventyTwoHours(int hoursRemaining, string expected)
    {
        var candidate = Task(
            createdDaysAgo: 35,
            dueInDays: hoursRemaining / 24.0,
            lastActivityHoursAgo: 72);

        var detected = ObserverRisk.Detect([candidate], Now, Defaults);

        Assert.Single(detected);
        Assert.Equal(expected, detected[0].Severity);
    }

    [Fact]
    public void RISK8_WeightIsWholeDaysRemaining_NeverNegativeAndCappedAtThirty()
    {
        // 6 ngày ⇒ weight 6.
        var sixDays = ObserverRisk.BuildSignal(
            ObserverRisk.Detect(
                [Task(createdDaysAgo: 30, dueInDays: 6, lastActivityHoursAgo: 72)], Now, Defaults),
            maxEvidenceIds: 10);

        Assert.NotNull(sixDays);
        Assert.Equal(6, sixDays!.Weight);

        // Dưới 1 ngày ⇒ weight 0 (làm tròn xuống), và TUYỆT ĐỐI không âm.
        var hours = ObserverRisk.BuildSignal(
            ObserverRisk.Detect(
                [Task(createdDaysAgo: 30, dueInDays: 0.4, lastActivityHoursAgo: 72)], Now, Defaults),
            maxEvidenceIds: 10);

        Assert.NotNull(hours);
        Assert.Equal(0, hours!.Weight);
        Assert.InRange(hours.Weight, 0, 30);

        // Xa hơn 30 ngày nhưng vẫn trong vùng 20% ⇒ kẹp ở 30, không tràn.
        var far = ObserverRisk.BuildSignal(
            ObserverRisk.Detect(
                [Task(createdDaysAgo: 400, dueInDays: 40, lastActivityHoursAgo: 72)], Now, Defaults),
            maxEvidenceIds: 10);

        Assert.NotNull(far);
        Assert.Equal(30, far!.Weight);
    }

    [Fact]
    public void RISK9_EvidenceIsCappedAndSortedMostUrgentFirst_ThenByTaskId()
    {
        // Ba task cùng rủi ro với mức khẩn cấp KHÁC nhau, cố tình truyền vào theo thứ tự lộn xộn.
        var later = Task(createdDaysAgo: 20, dueInDays: 3.5, lastActivityHoursAgo: 72);
        var soonest = Task(createdDaysAgo: 20, dueInDays: 0.3, lastActivityHoursAgo: 72);
        var middle = Task(createdDaysAgo: 20, dueInDays: 1.5, lastActivityHoursAgo: 72);

        var detected = ObserverRisk.Detect([later, soonest, middle], Now, Defaults);

        Assert.Equal(3, detected.Count);
        Assert.Equal(
            [soonest.TaskId, middle.TaskId, later.TaskId],
            detected.Select(c => c.TaskId));

        // Trần evidence được tôn trọng: giữ những task GẤP NHẤT, không phải những task đầu tiên.
        var signal = ObserverRisk.BuildSignal(detected, maxEvidenceIds: 2);
        Assert.NotNull(signal);
        Assert.Equal([soonest.TaskId, middle.TaskId], signal!.TaskIds);
        Assert.Equal(2, signal.TaskIds.Count);

        // Severity của signal = mức NẶNG NHẤT trong nhóm (một task gấp ⇒ cả workspace đáng báo động).
        Assert.Equal(NotificationSeverityExpectation(0.3), signal.Severity);
        Assert.Contains("3 task(s)", signal.Summary, StringComparison.Ordinal);
        Assert.Contains("giờ", signal.Summary, StringComparison.Ordinal);
    }

    // ---- RISK-12 / RISK-13: one signal, correct ordering ------------------

    [Fact]
    public void RISK12_ManyCandidatesProduceExactlyOneSignalNotOneEach()
    {
        var tasks = Enumerable.Range(0, 5)
            .Select(i => Task(createdDaysAgo: 20, dueInDays: 0.5 + (i * 0.1), lastActivityHoursAgo: 72))
            .ToList();

        var signals = ObserverSignalDetector.Analyze(
            new ObserverWorkspaceSnapshot(
                WorkspaceId,
                tasks,
                [new ObserverColumnSnapshot(tasks[0].ColumnId, tasks[0].BoardId, "Todo", IsDone: false)],
                Now),
            Defaults);

        // N task rủi ro ⇒ ĐÚNG MỘT cảnh báo (giống 4 detector gốc), không phải N thông báo.
        var signal = Assert.Single(
            signals.Signals,
            s => s.Type == NotificationTypes.AtRiskDeadline);

        Assert.Equal(5, signal.TaskIds.Count);
    }

    [Fact]
    public void RISK13_AnalyzeOrdersBySeverityThenWeightAndKeepsTiesDeterministic()
    {
        // Task trễ hạn nặng (Critical) + task sắp hết hạn còn 2 ngày (High) ⇒ Critical đứng trước High.
        // Cửa sổ 35 ngày để 48 h vẫn nằm trong vùng "còn < 20%" (48 h / 35 ngày ≈ 5.7%).
        var overdue = Task(createdDaysAgo: 30, dueInDays: -20, lastActivityHoursAgo: 72, title: "Overdue nặng");
        var risky = Task(createdDaysAgo: 35, dueInDays: 2, lastActivityHoursAgo: 72, title: "Sắp hết hạn");

        var snapshot = new ObserverWorkspaceSnapshot(
            WorkspaceId,
            [risky, overdue],
            [new ObserverColumnSnapshot(overdue.ColumnId, overdue.BoardId, "Todo", IsDone: false)],
            Now);

        var signals = ObserverSignalDetector.Analyze(snapshot, Defaults);
        var ordered = signals.Signals.ToList();

        // Mọi signal PHẢI có type thuộc whitelist, severity hợp lệ và weight không âm.
        Assert.All(ordered, s => Assert.True(NotificationTypes.IsKnown(s.Type), s.Type));
        Assert.All(ordered, s => Assert.InRange(s.Weight, 0, 1000));

        // Bất biến của thứ tự: severity KHÔNG được giảm dần ở bất kỳ cặp liền kề nào. Đây là điều mà
        // prompt và notification layer dựa vào để đưa tin quan trọng lên trước.
        for (var i = 1; i < ordered.Count; i++)
        {
            Assert.True(
                NotificationSeverities.Rank(ordered[i - 1].Severity)
                >= NotificationSeverities.Rank(ordered[i].Severity),
                $"Thứ tự severity sai: {Describe(ordered)}");
        }

        // Task trễ 20 ngày (Critical) phải được xếp TRƯỚC task sắp hết hạn (High).
        var criticalIndex = ordered.FindIndex(s => s.Severity == ObserverSeverity.Critical);
        var highIndex = ordered.FindIndex(s => s.Severity == ObserverSeverity.High);

        Assert.True(criticalIndex >= 0, $"Phải có ít nhất một signal Critical. Thực tế: {Describe(ordered)}");
        Assert.True(highIndex >= 0, $"Phải có ít nhất một signal High. Thực tế: {Describe(ordered)}");
        Assert.True(criticalIndex < highIndex, $"Critical phải đứng trước High. Thực tế: {Describe(ordered)}");

        // Chạy lại trên CÙNG snapshot nhưng đảo thứ tự đầu vào ⇒ kết quả y hệt (không phụ thuộc load order).
        var reordered = ObserverSignalDetector.Analyze(
            snapshot with { Tasks = [overdue, risky] },
            Defaults);

        Assert.Equal(
            signals.Signals.Select(s => (s.Type, s.Severity, s.Weight)),
            reordered.Signals.Select(s => (s.Type, s.Severity, s.Weight)));
    }

    // ---- RISK-14: vocabulary + prompt contract ----------------------------

    [Fact]
    public void RISK14_VocabularyAndPromptBothKnowTheNewType()
    {
        Assert.Equal(
            ["OverdueTask", "StalledTask", "AtRiskDeadline", "Overload", "Bottleneck"],
            NotificationTypes.All);

        Assert.Equal("AtRiskDeadline", NotificationTypes.Canonical("atriskdeadline"));
        Assert.True(NotificationTypes.IsKnown(NotificationTypes.AtRiskDeadline));

        // Prompt liệt kê CỨNG các type: thiếu một type ⇒ model không bao giờ phát ra nó ⇒ tính năng im lặng.
        var systemPrompt = ObserverPrompts.BuildSystemPrompt();
        Assert.Contains("AtRiskDeadline", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("OverdueTask", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("Bottleneck", systemPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public void RISK14b_ValidatorAcceptsAtRiskDeadlineAndStillDropsUnknownTypes()
    {
        var risky = Task(createdDaysAgo: 20, dueInDays: 0.5, lastActivityHoursAgo: 72);

        var signals = ObserverSignalDetector.Analyze(
            new ObserverWorkspaceSnapshot(
                WorkspaceId,
                [risky],
                [new ObserverColumnSnapshot(risky.ColumnId, risky.BoardId, "Todo", IsDone: false)],
                Now),
            Defaults);

        var output = new Modules.Ai.Contracts.AiObserverOutput
        {
            Findings =
            [
                new Modules.Ai.Contracts.AiObserverFinding
                {
                    Type = "atriskdeadline", // casing do model trả về, phải được chuẩn hoá
                    Severity = "High",
                    Title = "Sắp trễ hạn",
                    Message = "Cần rà soát ngay.",
                    TaskIds = [risky.TaskId],
                },
                new Modules.Ai.Contracts.AiObserverFinding
                {
                    Type = "SomethingElse", // không có trong whitelist ⇒ bị bỏ
                    Severity = "Critical",
                    TaskIds = [risky.TaskId],
                },
            ],
        };

        var findings = ObserverFindingValidator.Validate(output, signals.Signals, new ObserverOptions());

        var finding = Assert.Single(findings);
        Assert.Equal(NotificationTypes.AtRiskDeadline, finding.Type);
        Assert.Equal([risky.TaskId], finding.TaskIds);
    }

    [Fact]
    public void RISK14c_PayloadCountsSignalsSoTruncationIsVisible()
    {
        // P4: đếm số tín hiệu THẬT SỰ vào được prompt, để việc cắt bớt không xảy ra âm thầm.
        var risky = Task(createdDaysAgo: 20, dueInDays: 0.5, lastActivityHoursAgo: 72);
        var snapshot = new ObserverWorkspaceSnapshot(
            WorkspaceId,
            [risky],
            [new ObserverColumnSnapshot(risky.ColumnId, risky.BoardId, "Todo", IsDone: false)],
            Now);

        var signals = ObserverSignalDetector.Analyze(snapshot, Defaults);
        var request = ObserverSummarizer.BuildRequest(snapshot, signals, new ObserverOptions());

        Assert.Equal(signals.Signals.Count, ObserverSummarizer.CountSignalsInPayload(request.UserPrompt));

        // Prompt không phải payload ⇒ trả 0 chứ không ném (một bộ đếm chẩn đoán không được làm chết lượt quét).
        Assert.Equal(0, ObserverSummarizer.CountSignalsInPayload("không phải JSON"));
        Assert.Equal(0, ObserverSummarizer.CountSignalsInPayload(string.Empty));
    }

    // ---- helpers ----------------------------------------------------------

    /// <summary>
    /// The severity the rule is expected to produce for a deadline this many days away. Mirrors the
    /// documented 24 h / 72 h ladder.
    /// </summary>
    private static string NotificationSeverityExpectation(double daysRemaining)
        => daysRemaining <= 1 ? ObserverSeverity.Critical : ObserverSeverity.Medium;

    /// <summary>One at-risk task as a work-item, with the timestamps the row already carries.</summary>
    private static ProjectWorkItem WorkItem(ObserverTaskSnapshot task)
        => new(task.TaskId, task.CreatedAt, task.DueDate, ActivityAt(task));

    /// <summary>
    /// Mirrors <c>ObserverSignalDetector.ActivityAt</c> (the Ai module keeps that helper
    /// <c>internal</c>, so the suite re-states the same three-way rule here): a comment newer than the
    /// last edit counts as movement, otherwise the later of creation and last edit.
    /// </summary>
    private static DateTimeOffset ActivityAt(ObserverTaskSnapshot task)
    {
        if (task.LastCommentAt.HasValue && task.LastCommentAt.Value > task.UpdatedAt)
        {
            return task.LastCommentAt.Value;
        }

        return task.UpdatedAt > task.CreatedAt ? task.UpdatedAt : task.CreatedAt;
    }

    /// <summary>Diagnostic rendering of a signal set, so a failing ordering assertion says what it saw.</summary>
    private static string Describe(IReadOnlyList<ObserverSignal> signals)
        => signals.Count == 0
            ? "<rỗng>"
            : string.Join(" | ", signals.Select(s => $"{s.Type}/{s.Severity}/w={s.Weight}/tasks={s.TaskIds.Count}"));

    /// <summary>Stable workspace id per test class, so a snapshot reads realistically.</summary>
    private static readonly Guid WorkspaceId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    /// <summary>
    /// One open task with full control over the three instants the rule cares about. All offsets are
    /// expressed relative to the frozen <see cref="Now"/> so the assertions read like the rule itself.
    /// </summary>
    private static ObserverTaskSnapshot Task(
        double createdDaysAgo,
        double? dueInDays,
        double lastActivityHoursAgo,
        double? lastCommentHoursAgo = null,
        bool isDone = false,
        string title = "Task rủi ro")
    {
        var createdAt = Now.AddDays(-createdDaysAgo);
        var updatedAt = Now.AddHours(-lastActivityHoursAgo);
        var lastCommentAt = lastCommentHoursAgo.HasValue ? Now.AddHours(-lastCommentHoursAgo.Value) : (DateTimeOffset?)null;

        return new ObserverTaskSnapshot(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Board chính",
            Guid.NewGuid(),
            isDone ? "Done" : "Todo",
            title,
            // Một assignee cố định để nhánh evidence `userIds` có gì đó để kiểm tra.
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "Nguyễn Văn A",
            dueInDays.HasValue ? Now.AddDays(dueInDays.Value) : null,
            createdAt,
            updatedAt,
            isDone,
            lastCommentAt.HasValue ? 1 : 0,
            lastCommentAt);
    }
}
