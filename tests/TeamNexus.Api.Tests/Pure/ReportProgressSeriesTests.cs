using TeamNexus.Modules.Reporting.Contracts;
using TeamNexus.Modules.Reporting.Services;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 13 §2 — <see cref="ReportAggregator.BuildProgressSeries"/> (PS-1…PS-10).
/// <para>
/// Pure by construction: a hand-built <see cref="ReportWorkspaceSnapshot"/>, no database, no HTTP. That
/// is what makes the awkward cases affordable to assert exactly — a timezone boundary, a task that
/// predates the window, a 365-day range that must flip to weekly buckets — instead of settling for the
/// comfortable middle of a range.
/// </para>
/// </summary>
public sealed class ReportProgressSeriesTests
{
    /// <summary>Window used by most cases: 7 days ⇒ daily buckets (well under the weekly threshold).</summary>
    private static readonly DateTimeOffset Start = new(2026, 1, 10, 0, 0, 0, TimeSpan.Zero);

    private static readonly DateTimeOffset End = new(2026, 1, 16, 0, 0, 0, TimeSpan.Zero);

    private static readonly ReportThresholds Defaults = ReportThresholds.Default;

    // ---- PS-1 … PS-3: open/completed accounting ----------------------------

    [Fact]
    public void PS1_TaskCreatedAndClosedInWindow_CountsAsCreatedThenOpenThenCompleted()
    {
        // `Day(n)` = Start + n ngày ⇒ bucket[n] mang đúng ngày đó.
        var series = Build(
            Tasks(Task(created: Day(1, 9), completed: Day(2, 15))),
            Start,
            End);

        Assert.Equal(7, series.Days.Count);

        // Bucket 0 = Start (2026-01-10): task chưa tồn tại.
        Assert.Equal(new DateOnly(2026, 1, 10), series.Days[0].Date);
        Assert.Equal(0, series.Days[0].Creations);
        Assert.Equal(0, series.Days[0].OpenTasks);

        // Bucket 1 = 2026-01-11: được tạo ⇒ có `creations` và còn mở ở cuối ngày.
        Assert.Equal(new DateOnly(2026, 1, 11), series.Days[1].Date);
        Assert.Equal(1, series.Days[1].Creations);
        Assert.Equal(1, series.Days[1].OpenTasks);
        Assert.Equal(0, series.Days[1].Completions);

        // Bucket 2 = 2026-01-12: hoàn thành ⇒ có `completions` VÀ không còn mở ở cuối ngày.
        Assert.Equal(new DateOnly(2026, 1, 12), series.Days[2].Date);
        Assert.Equal(1, series.Days[2].Completions);
        Assert.Equal(0, series.Days[2].OpenTasks);

        // Các ngày sau vẫn đóng.
        Assert.All(series.Days.Skip(2), day => Assert.Equal(0, day.OpenTasks));
    }

    [Fact]
    public void PS2_TaskCreatedBeforeWindowAndStillOpen_KeepsTheBaseline()
    {
        // Created 5 days BEFORE `Start`, never completed: it must raise openTasks on day 1 — a burndown
        // chart that forgets the backlog it started with is worse than no chart at all.
        var series = Build(Tasks(Task(created: Start.AddDays(-5))), Start, End);

        Assert.Equal(1, series.Days[0].OpenTasks);
        Assert.Equal(1, series.Days[^1].OpenTasks);
        Assert.All(series.Days, day => Assert.Equal(0, day.Creations));
    }

    [Fact]
    public void PS3_TaskCreatedBeforeWindowAndClosedInside_AppearsInBothSeries()
    {
        // Created before the window, completed on bucket 1 (= Start + 1 ngày): open on bucket 0,
        // completed on bucket 1.
        var series = Build(
            Tasks(Task(created: Start.AddDays(-10), completed: Day(1, 12))),
            Start,
            End);

        Assert.Equal(1, series.Days[0].OpenTasks);
        Assert.Equal(0, series.Days[0].Completions);
        Assert.Equal(1, series.Days[1].Completions);
        Assert.Equal(0, series.Days[1].OpenTasks);
        Assert.All(series.Days, day => Assert.Equal(0, day.Creations));
    }

    // ---- PS-4 … PS-5: timezone --------------------------------------------

    [Fact]
    public void PS4_TzOffsetMovesTheCompletionIntoTheViewersDay()
    {
        // 2026-01-20T18:00Z. In UTC that is the 20th; at UTC+7 it is already 01:00 on the 21st, which is
        // the day the user actually saw on their own clock.
        var completedAt = new DateTimeOffset(2026, 1, 20, 18, 0, 0, TimeSpan.Zero);
        var windowStart = new DateTimeOffset(2026, 1, 19, 0, 0, 0, TimeSpan.Zero);
        var windowEnd = new DateTimeOffset(2026, 1, 22, 0, 0, 0, TimeSpan.Zero);

        var utc = Build(Tasks(Task(created: windowStart, completed: completedAt)), windowStart, windowEnd, tz: 0);
        var local = Build(Tasks(Task(created: windowStart, completed: completedAt)), windowStart, windowEnd, tz: 420);

        Assert.Equal(new DateOnly(2026, 1, 20), utc.Days.Single(d => d.Completions == 1).Date);
        Assert.Equal(new DateOnly(2026, 1, 21), local.Days.Single(d => d.Completions == 1).Date);
    }

    [Theory]
    [InlineData(99999, 840)]
    [InlineData(-99999, -840)]
    [InlineData(420, 420)]
    [InlineData(0, 0)]
    public void PS5_TzOffsetIsClampedAndEchoed(int requested, int expected)
    {
        var series = Build(Tasks(), Start, End, tz: requested);

        // Clamped, never rejected: this is a UI knob, not a contract (same rule as `take`).
        Assert.Equal(expected, series.TzOffsetMinutes);
    }

    [Fact]
    public void PS6_OpenTasksIsNeverNegative_AndIsNotReducedBySameDayCompletions()
    {
        // Bucket 0: one task was already open (created before the window) and another is created and
        // closed the same day. `openTasks` is "open at the END of the day", so it stays 1 — it is NOT
        // reduced by the completion, which is exactly what keeps `openTasks - completions`
        // (the ideal line) >= 0.
        var series = Build(
            Tasks(
                Task(created: Start.AddDays(-3)),
                Task(created: Day(0, 8), completed: Day(0, 20))),
            Start,
            End);

        Assert.Equal(1, series.Days[0].OpenTasks);
        Assert.Equal(1, series.Days[0].Completions);
        Assert.All(series.Days, day => Assert.True(day.OpenTasks >= 0));
        Assert.All(series.Days, day => Assert.True(day.Completions >= 0));
        Assert.True(series.Days[0].OpenTasks - series.Days[0].Completions >= 0);
    }

    // ---- PS-7: done column without completed_at ----------------------------

    [Fact]
    public void PS7_TaskInDoneColumnWithoutCompletedAt_IsNeverOpenAndNeverCompleted()
    {
        // Legacy data: the task sits in an is_done column but carries no completed_at. There is no honest
        // closing date, so it must not be counted as a completion on any day — and, having no closing
        // date we can trust, it must not inflate `openTasks` either.
        var series = Build(
            Tasks(Task(created: Start.AddDays(-2), completed: null, isDoneColumn: true)),
            Start,
            End);

        Assert.All(series.Days, day => Assert.Equal(0, day.Completions));
        Assert.All(series.Days, day => Assert.Equal(0, day.OpenTasks));
        Assert.Equal(0, series.Velocity.CompletedInRange);
    }

    // ---- PS-9: mode selection, caps, empty input ---------------------------

    [Fact]
    public void PS9a_ExactlySixtyDays_StaysDaily()
    {
        var range = RangeOfDays(60);
        var series = Build(Tasks(), range.From, range.To);

        Assert.Equal(ReportSeriesModes.Date, series.Mode);
        Assert.Equal(1, series.BucketDays);
        Assert.Equal(60, series.Days.Count);
        Assert.Empty(series.Weeks);
        Assert.False(series.BucketCapReached);
    }

    [Fact]
    public void PS9b_SixtyOneDays_SwitchesToWeeklyBuckets()
    {
        var range = RangeOfDays(61);
        var series = Build(Tasks(), range.From, range.To);

        Assert.Equal(ReportSeriesModes.Week, series.Mode);
        Assert.Equal(7, series.BucketDays);
        Assert.Empty(series.Days);
        Assert.Equal(10, series.Weeks.Count); // 61 ngày lịch trải trên 10 tuần (mốc Thứ Hai)
    }

    [Fact]
    public void PS9c_RangeBeyondTheBucketCap_TruncatesToTheLastBuckets()
    {
        var range = RangeOfDays(365);
        var series = Build(Tasks(), range.From, range.To);

        // A 365-day range is too long to draw day by day, so it goes weekly and the cap never bites.
        Assert.Equal(ReportSeriesModes.Week, series.Mode);
        Assert.False(series.BucketCapReached);

        // Force the daily path with a tiny cap to prove the truncation flag and the "keep the newest"
        // rule: a chart that silently dropped the most recent days would be actively misleading.
        var capped = Build(
            Tasks(Task(created: range.From.AddDays(80))),
            range.From,
            range.From.AddDays(119),
            thresholds: Defaults with { MaxSeriesBuckets = 10, SeriesWeeklyThresholdDays = 365 });

        Assert.Equal(ReportSeriesModes.Date, capped.Mode);
        Assert.True(capped.BucketCapReached);
        Assert.Equal(10, capped.Days.Count);
        Assert.Equal(10, capped.MaxBuckets);
        Assert.Equal(DateOnly.FromDateTime(range.From.AddDays(119).UtcDateTime), capped.Days[^1].Date);
    }

    [Fact]
    public void PS9d_WeeklyBucketsStartOnMonday()
    {
        // 2026-01-10 is a Saturday; its week must start Monday 2026-01-05.
        var range = RangeOfDays(61);
        var series = Build(Tasks(), range.From, range.To);

        Assert.Equal(new DateOnly(2026, 1, 5), series.Weeks[0].WeekStart);
        Assert.All(series.Weeks, week => Assert.Equal(DayOfWeek.Monday, week.WeekStart.DayOfWeek));
    }

    // ---- PS-10: velocity, definitions, empty input -------------------------

    [Fact]
    public void PS10_VelocityAveragesPerWeek_AndDefinitionsCoverEverySeriesKey()
    {
        // Four completions inside a 7-day window ⇒ 4.0 per week (7 / 7 = 1 week).
        var series = Build(
            Tasks(
                Task(created: Day(0, 8), completed: Day(1, 9)),
                Task(created: Day(0, 8), completed: Day(2, 9)),
                Task(created: Day(0, 8), completed: Day(3, 9)),
                Task(created: Day(0, 8), completed: Day(4, 9)),
                Task(created: Day(0, 8))),
            Start,
            End);

        Assert.Equal(4, series.Velocity.CompletedInRange);
        Assert.Equal(4.0, series.Velocity.AvgCompletionsPerWeek);
        Assert.Equal(1, series.Velocity.OpenAtEnd);

        var definitions = ReportAggregator.ProgressSeriesMetricDefinitions();
        foreach (var key in new[] { "openTasks", "completions", "creations", "avgCompletionsPerWeek", "openAtEnd", "bucketDays", "idealOpenSeries" })
        {
            Assert.True(definitions.ContainsKey(key), $"Thiếu mô tả cho chỉ số '{key}'.");
            Assert.False(string.IsNullOrWhiteSpace(definitions[key]));
        }
    }

    [Fact]
    public void PS10b_EmptyInputProducesZeroesNotNaNsAndKeepsEveryBucket()
    {
        // A workspace with no tasks at all must still render a continuous, all-zero chart.
        var series = Build(Tasks(), Start, End);

        Assert.Equal(7, series.Days.Count);
        Assert.All(series.Days, day =>
        {
            Assert.Equal(0, day.OpenTasks);
            Assert.Equal(0, day.Completions);
            Assert.Equal(0, day.Creations);
        });

        Assert.Equal(0, series.Velocity.CompletedInRange);
        Assert.Equal(0, series.Velocity.OpenAtEnd);
        Assert.Equal(0.0, series.Velocity.AvgCompletionsPerWeek);
        Assert.True(double.IsFinite(series.Velocity.AvgCompletionsPerWeek));
    }

    [Fact]
    public void PS10c_BucketsAreAscendingAndDeterministic()
    {
        var series = Build(Tasks(Task(created: Day(0, 9))), Start, End);

        var dates = series.Days.Select(d => d.Date).ToList();
        Assert.Equal(dates.OrderBy(d => d).ToList(), dates);

        // Same input ⇒ identical output (no clock, no randomness).
        var again = Build(Tasks(Task(created: Day(0, 9))), Start, End);
        Assert.Equal(dates, again.Days.Select(d => d.Date).ToList());
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>
    /// A task at hour <paramref name="hour"/> (UTC) of day <paramref name="index"/> of the default window.
    /// <b>Hour defaults to 0</b> — not to midday — so that <c>Day(n, 0)</c> is exactly the day bucket the
    /// assertions talk about, and a timezone-agnostic case cannot drift a day by accident.
    /// </summary>
    private static DateTimeOffset Day(int index, int hour = 0)
        => Start.AddDays(index).AddHours(hour);

    private static (DateTimeOffset From, DateTimeOffset To) RangeOfDays(int days)
        => (Start, Start.AddDays(days - 1));

    private static ReportProgressSeries Build(
        IReadOnlyList<ReportTaskSnapshot> tasks,
        DateTimeOffset from,
        DateTimeOffset to,
        int? tz = null,
        ReportThresholds? thresholds = null)
    {
        var boards = new[] { new ReportBoardSnapshot(BoardId, "Board chính") };

        // `Days` đếm theo NGÀY LỊCH bao gồm cả hai đầu (đúng cách chuỗi thời gian đếm bucket), không phải
        // (to − from) — nếu không, cửa sổ 7 ngày sẽ tự nhận là 6 và mọi khẳng định về mode lệch một ngày.
        var days = DateOnly.FromDateTime(to.UtcDateTime).DayNumber
                   - DateOnly.FromDateTime(from.UtcDateTime).DayNumber + 1;
        var range = new ReportRange(from, to, Math.Max(1, days), false, "test");

        var snapshot = new ReportWorkspaceSnapshot(
            WorkspaceId,
            "Workspace test",
            range,
            boards,
            [],
            tasks,
            new ReportActivitySnapshot(0, 0, []),
            [],
            0,
            to);

        return ReportAggregator.BuildProgressSeries(snapshot, thresholds ?? Defaults, tz);
    }

    private static IReadOnlyList<ReportTaskSnapshot> Tasks(params ReportTaskSnapshot[] tasks) => tasks;

    private static readonly Guid WorkspaceId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly Guid BoardId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ReportTaskSnapshot Task(
        DateTimeOffset created,
        DateTimeOffset? completed = null,
        bool isDoneColumn = false)
        => new(
            Guid.NewGuid(),
            BoardId,
            "Board chính",
            Guid.NewGuid(),
            isDoneColumn ? "Done" : "Todo",
            isDoneColumn,
            "Task",
            null,
            null,
            null,
            null,
            created,
            created,
            completed);
}
