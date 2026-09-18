using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 14 §3.2 — <see cref="ProjectHealth.Compute"/> (HEALTH-1…HEALTH-12).
/// <para>
/// Pure by construction: hand-built <see cref="ProjectHealthInput"/> counts, no database, no HTTP, no
/// clock. That is what makes the awkward cases affordable to assert <b>exactly</b> — the band
/// boundaries, a zero denominator, a workspace whose every task is late — instead of settling for the
/// comfortable middle of a range.
/// </para>
/// </summary>
public sealed class ProjectHealthTests
{
    /// <summary>A workspace with nothing wrong with it: 30 open, none late, nobody overloaded.</summary>
    private static ProjectHealthInput Healthy => new(
        TotalTasks: 50,
        OpenTasks: 30,
        OverdueTasks: 0,
        AtRiskTasks: 0,
        StalledTasks: 0,
        OldestOpenTaskAgeDays: 0,
        MaxOpenTasksPerAssignee: 1,
        AssigneeCount: 5);

    // ---- HEALTH-1 / HEALTH-5: totality ------------------------------------

    [Fact]
    public void HEALTH1_EmptyWorkspace_ScoresFullMarksWithZeroComponents()
    {
        var result = ProjectHealth.Compute(new ProjectHealthInput(0, 0, 0, 0, 0, 0, 0, 0));

        Assert.Equal(100, result.Score);
        Assert.Equal(ProjectHealthBands.Good, result.Band);

        // Tất cả components phải là 0 và KHÔNG được là NaN (chia cho 0 phải được guard).
        Assert.All(result.Components.Values, value => Assert.Equal(0, value));
        Assert.DoesNotContain(result.Components.Values, double.IsNaN);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void HEALTH5_ZeroOpenTasks_NeverDividesByZeroAndIgnoresContradictoryCounters()
    {
        // Dữ liệu vô lý (có task quá hạn/tồn lâu nhưng 0 task mở) KHÔNG được làm vỡ công thức VÀ không
        // được trừ điểm: đây là lưới an toàn cho một lỗi đếm ở tầng trên, và câu trả lời đúng là
        // "không có việc đang mở ⇒ không có gì để chậm". (Hai khoản `aging`/`load` không có mẫu số nên
        // phải được gate tường minh — nếu chỉ dựa vào ratio thì chúng vẫn trừ điểm.)
        var result = ProjectHealth.Compute(new ProjectHealthInput(5, 0, 3, 2, 1, 400, 4, 2));

        Assert.Equal(100, result.Score);
        Assert.All(result.Components.Values, value => Assert.Equal(0, value));
        Assert.DoesNotContain(result.Components.Values, double.IsInfinity);
        Assert.Empty(result.Reasons);
    }

    [Fact]
    public void HEALTH5b_AgingAndLoadOnlyApplyWhenThereIsOpenWork()
    {
        // Cùng counters, chỉ khác "có việc mở hay không": chứng minh gate là thật, không phải trùng hợp.
        var withOpenWork = ProjectHealth.Compute(new ProjectHealthInput(5, 5, 0, 0, 0, 400, 50, 1));
        var withoutOpenWork = ProjectHealth.Compute(new ProjectHealthInput(5, 0, 0, 0, 0, 400, 50, 1));

        Assert.Equal(ProjectHealth.AgingWeight + ProjectHealth.LoadWeight, withOpenWork.Components["aging"]
            + withOpenWork.Components["load"]);
        Assert.Equal(0, withoutOpenWork.Components["aging"]);
        Assert.Equal(0, withoutOpenWork.Components["load"]);
        Assert.True(withOpenWork.Score < withoutOpenWork.Score);
    }

    // ---- HEALTH-2 / HEALTH-3: the overdue weight --------------------------

    [Fact]
    public void HEALTH2_EveryOpenTaskOverdue_DeductsTheFullOverdueWeight()
    {
        var result = ProjectHealth.Compute(Healthy with { OverdueTasks = 30 });

        Assert.Equal(ProjectHealth.OverdueWeight, result.Components["overdue"]);
        Assert.Equal(100 - ProjectHealth.OverdueWeight, result.Score);
    }

    [Fact]
    public void HEALTH3_HalfTheOpenTasksOverdue_DeductsHalfTheOverdueWeight()
    {
        var result = ProjectHealth.Compute(Healthy with { OverdueTasks = 15 });

        Assert.Equal(ProjectHealth.OverdueWeight / 2, result.Components["overdue"]);
        Assert.Equal(80, result.Score);
    }

    // ---- HEALTH-4: clamping ----------------------------------------------

    [Fact]
    public void HEALTH4_EveryPenaltyAtMaximum_ScoresZeroAndNeverGoesNegative()
    {
        var result = ProjectHealth.Compute(new ProjectHealthInput(
            TotalTasks: 100,
            OpenTasks: 10,
            OverdueTasks: 10,
            AtRiskTasks: 10,
            StalledTasks: 10,
            OldestOpenTaskAgeDays: 400,
            MaxOpenTasksPerAssignee: 50,
            AssigneeCount: 1));

        // Mọi khoản trừ đều chạm trần riêng của nó, và 5 trần cộng lại bằng ĐÚNG 100 ⇒ điểm 0.
        Assert.Equal(ProjectHealth.OverdueWeight, result.Components["overdue"]);
        Assert.Equal(ProjectHealth.AtRiskWeight, result.Components["atRisk"]);
        Assert.Equal(ProjectHealth.StalledWeight, result.Components["stalled"]);
        Assert.Equal(ProjectHealth.AgingWeight, result.Components["aging"]);
        Assert.Equal(ProjectHealth.LoadWeight, result.Components["load"]);
        Assert.Equal(0, result.Score);
        Assert.Equal(ProjectHealthBands.Critical, result.Band);

        // Một khoản trừ duy nhất ở trần ⇒ đúng bằng phần còn lại của thang điểm.
        var onlyOverdue = ProjectHealth.Compute(Healthy with { OverdueTasks = 30 });
        Assert.Equal(100 - ProjectHealth.OverdueWeight, onlyOverdue.Score);

        // Và điểm không bao giờ âm dù đầu vào có vượt mọi trần hàng nghìn lần.
        var absurd = ProjectHealth.Compute(new ProjectHealthInput(999, 1, 999, 999, 999, 99999, 999, 1));
        Assert.Equal(0, absurd.Score);
        Assert.Equal(ProjectHealthBands.Critical, absurd.Band);
    }

    [Fact]
    public void HEALTH4b_TheFiveWeightsAddUpToOneHundredSoTheScaleIsFullyUsable()
    {
        // Bất biến của thang điểm: 0 phải CHẠM TỚI ĐƯỢC (nếu 5 trần cộng lại < 100 thì không workspace
        // nào có thể đạt 0, và mọi ngưỡng `band` phía dưới sẽ vô nghĩa).
        var total = ProjectHealth.OverdueWeight
            + ProjectHealth.AtRiskWeight
            + ProjectHealth.StalledWeight
            + ProjectHealth.AgingWeight
            + ProjectHealth.LoadWeight;

        Assert.Equal(100d, total);

        // Mỗi trần phải dương và không khoản nào một mình chiếm quá nửa thang điểm.
        Assert.All(
            new[]
            {
                ProjectHealth.OverdueWeight, ProjectHealth.AtRiskWeight, ProjectHealth.StalledWeight,
                ProjectHealth.AgingWeight, ProjectHealth.LoadWeight,
            },
            weight => Assert.InRange(weight, 1, 50));
    }

    // ---- HEALTH-6: band boundaries ---------------------------------------

    [Theory]
    [InlineData(100, ProjectHealthBands.Good)]
    [InlineData(80, ProjectHealthBands.Good)]      // biên dưới của "Tốt" ĐƯỢC tính là Tốt
    [InlineData(79, ProjectHealthBands.Watch)]
    [InlineData(60, ProjectHealthBands.Watch)]     // biên dưới của "Cần chú ý"
    [InlineData(59, ProjectHealthBands.Risky)]
    [InlineData(40, ProjectHealthBands.Risky)]     // biên dưới của "Rủi ro"
    [InlineData(39, ProjectHealthBands.Critical)]
    [InlineData(0, ProjectHealthBands.Critical)]
    public void HEALTH6_BandFor_PinsEveryThresholdBoundary(int score, string expected)
    {
        Assert.Equal(expected, ProjectHealth.BandFor(score));
    }

    [Fact]
    public void HEALTH6b_Compute_ReportsTheSameBandAsBandFor()
    {
        // Chống lệch giữa "ngưỡng tra riêng" và "ngưỡng dùng khi tính điểm".
        var result = ProjectHealth.Compute(Healthy with { OverdueTasks = 15 });

        Assert.Equal(80, result.Score);
        Assert.Equal(ProjectHealth.BandFor(80), result.Band);
    }

    // ---- HEALTH-7 / HEALTH-8 / HEALTH-9: aging and load -------------------

    [Fact]
    public void HEALTH7_OldestOpenTaskAtThirtyDays_DeductsTheFullAgingWeight()
    {
        var atReference = ProjectHealth.Compute(Healthy with { OldestOpenTaskAgeDays = 30 });
        Assert.Equal(ProjectHealth.AgingWeight, atReference.Components["aging"]);
        Assert.Equal(85, atReference.Score);

        // Nửa đường ⇒ nửa điểm trừ (tuyến tính, không bậc thang).
        var halfway = ProjectHealth.Compute(Healthy with { OldestOpenTaskAgeDays = 15 });
        Assert.Equal(ProjectHealth.AgingWeight / 2, halfway.Components["aging"]);

        // Quá ngưỡng ⇒ kẹp ở trần, không vượt.
        var wayOver = ProjectHealth.Compute(Healthy with { OldestOpenTaskAgeDays = 365 });
        Assert.Equal(ProjectHealth.AgingWeight, wayOver.Components["aging"]);
    }

    [Fact]
    public void HEALTH8_OneAssigneeHoldingFiveOrMoreOpenTasks_DeductsTheFullLoadWeight()
    {
        var saturated = ProjectHealth.Compute(Healthy with { MaxOpenTasksPerAssignee = 5, AssigneeCount = 1 });
        Assert.Equal(ProjectHealth.LoadWeight, saturated.Components["load"]);

        // 1 task mở/người là trạng thái lành mạnh ⇒ miễn nhiễm hoàn toàn.
        var free = ProjectHealth.Compute(Healthy with { MaxOpenTasksPerAssignee = 1, AssigneeCount = 1 });
        Assert.Equal(0, free.Components["load"]);

        // Chín task vẫn chỉ ăn trần (không tuyến tính vô hạn theo số task).
        var over = ProjectHealth.Compute(Healthy with { MaxOpenTasksPerAssignee = 9, AssigneeCount = 1 });
        Assert.Equal(ProjectHealth.LoadWeight, over.Components["load"]);

        // Ba task/người ⇒ một nửa trần.
        var half = ProjectHealth.Compute(Healthy with { MaxOpenTasksPerAssignee = 3, AssigneeCount = 1 });
        Assert.Equal(ProjectHealth.LoadWeight / 2, half.Components["load"]);
    }

    [Fact]
    public void HEALTH9_NoAssignees_MeansLoadCannotBeABottleneck()
    {
        var result = ProjectHealth.Compute(Healthy with { AssigneeCount = 0, MaxOpenTasksPerAssignee = 7 });

        Assert.Equal(0, result.Components["load"]);
        Assert.Equal(100, result.Score);
    }

    // ---- HEALTH-10 / HEALTH-11: the contract shape ------------------------

    [Fact]
    public void HEALTH10_ComponentsExposeExactlyTheFiveDocumentedKeys()
    {
        var result = ProjectHealth.Compute(Healthy);

        Assert.Equal(
            ["aging", "atRisk", "load", "overdue", "stalled"],
            result.Components.Keys.OrderBy(k => k, StringComparer.Ordinal));

        // Khoá phải là camelCase ỔN ĐỊNH: frontend hiển thị nhãn tiếng Việt theo đúng 5 khoá này.
        Assert.Contains("overdue", result.Components.Keys);
        Assert.Contains("atRisk", result.Components.Keys);
        Assert.Contains("stalled", result.Components.Keys);
        Assert.Contains("aging", result.Components.Keys);
        Assert.Contains("load", result.Components.Keys);
    }

    [Fact]
    public void HEALTH11_ReasonsAreEmptyWhenNothingIsPenalisedAndListOnlyRealProblems()
    {
        // Workspace khỏe ⇒ KHÔNG bịa lý do (UI dựa vào đây để quyết định ẩn cả khối).
        Assert.Empty(ProjectHealth.Compute(Healthy).Reasons);

        var unhealthy = ProjectHealth.Compute(new ProjectHealthInput(
            TotalTasks: 10,
            OpenTasks: 10,
            OverdueTasks: 2,
            AtRiskTasks: 3,
            StalledTasks: 4,
            OldestOpenTaskAgeDays: 45,
            MaxOpenTasksPerAssignee: 6,
            AssigneeCount: 1));

        Assert.Equal(5, unhealthy.Reasons.Count);

        // Lý do phải là tiếng Việt có dấu và nêu CON SỐ thật, không phải câu chung chung.
        Assert.Contains(unhealthy.Reasons, r => r.Contains("2 thẻ quá hạn", StringComparison.Ordinal));
        Assert.Contains(unhealthy.Reasons, r => r.Contains("3 thẻ sắp hết hạn", StringComparison.Ordinal));
        Assert.Contains(unhealthy.Reasons, r => r.Contains("4 thẻ đứng yên", StringComparison.Ordinal));
        Assert.Contains(unhealthy.Reasons, r => r.Contains("45 ngày", StringComparison.Ordinal));
        Assert.Contains(unhealthy.Reasons, r => r.Contains("6 thẻ mở", StringComparison.Ordinal));

        // Chỉ khoá có điểm trừ mới sinh lý do.
        var onlyOverdue = ProjectHealth.Compute(Healthy with { OverdueTasks = 1 });
        Assert.Single(onlyOverdue.Reasons);
    }

    // ---- HEALTH-12: determinism over a generated grid ---------------------

    [Fact]
    public void HEALTH12_ScoreIsAlwaysAWholeNumberInRangeForAnyPlausibleInput()
    {
        // Lưới sinh tất định (không random ⇒ không flaky) phủ cả hai đầu của mọi trục.
        int[] counts = [0, 1, 7, 30];
        int[] ages = [0, 1, 30, 365];
        int[] loads = [0, 1, 5, 50];

        var checkedCases = 0;

        foreach (var open in counts)
        {
            foreach (var overdue in counts)
            {
                foreach (var atRisk in counts)
                {
                    foreach (var stalled in counts)
                    {
                        foreach (var age in ages)
                        {
                            foreach (var load in loads)
                            {
                                var result = ProjectHealth.Compute(new ProjectHealthInput(
                                    TotalTasks: open + overdue,
                                    OpenTasks: open,
                                    OverdueTasks: overdue,
                                    AtRiskTasks: atRisk,
                                    StalledTasks: stalled,
                                    OldestOpenTaskAgeDays: age,
                                    MaxOpenTasksPerAssignee: load,
                                    AssigneeCount: load == 0 ? 0 : 2));

                                Assert.InRange(result.Score, 0, 100);
                                Assert.Contains(
                                    result.Band,
                                    new[]
                                    {
                                        ProjectHealthBands.Good, ProjectHealthBands.Watch,
                                        ProjectHealthBands.Risky, ProjectHealthBands.Critical,
                                    });

                                foreach (var component in result.Components.Values)
                                {
                                    Assert.False(double.IsNaN(component));
                                    Assert.False(double.IsInfinity(component));
                                    // Không khoản trừ nào được vượt trần lớn nhất của công thức.
                                    Assert.InRange(component, 0, ProjectHealth.OverdueWeight);
                                }

                                checkedCases++;
                            }
                        }
                    }
                }
            }
        }

        Assert.Equal(4 * 4 * 4 * 4 * 4 * 4, checkedCases);
    }

    // ---- cross-check with the shared risk rule ----------------------------

    [Fact]
    public void HEALTH_AcceptingRiskCountsIsWhatKeepsTheGaugeAndTheObserverAligned()
    {
        // AtRiskTasks đi vào công thức dưới dạng CON SỐ đã đếm sẵn bằng DeadlineRiskRules (quy tắc
        // dùng chung với AtRiskDeadline) — test này khoá lại phần "vì sao cùng một đại lượng".
        var noRisk = ProjectHealth.Compute(Healthy with { AtRiskTasks = 0 });
        var someRisk = ProjectHealth.Compute(Healthy with { AtRiskTasks = 6 });

        Assert.Equal(0, noRisk.Components["atRisk"]);
        Assert.Equal(100, noRisk.Score);

        // 6/30 = 20% ⇒ 20 × 0.2 = 4 điểm trừ.
        Assert.Equal(4, someRisk.Components["atRisk"]);
        Assert.Equal(96, someRisk.Score);
    }
}
