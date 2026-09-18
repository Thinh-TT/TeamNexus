namespace TeamNexus.Modules.Board.DTOs;

/// <summary>
/// One row of a dashboard "Task của tôi" bucket (Phase 12 §1).
/// <para>
/// Deliberately <b>not</b> a <c>TaskResponse</c>: the dashboard needs board/column <b>names</b> (a
/// card shown outside its board has no context otherwise) and a pre-computed <c>overdueByDays</c>,
/// while it does not need labels, comment counts or agent-run state. Keeping a separate shape also
/// leaves the verified <c>TaskResponse</c> contract untouched (§2 P4).
/// </para>
/// </summary>
/// <param name="IsDone">
/// In an <c>is_done</c> column OR has <c>completed_at</c> — exactly
/// <c>ReportAggregator.IsDone</c> (Phase 6 §1.3), so the dashboard and the exported report can never
/// disagree about how many tasks are done.
/// </param>
/// <param name="OverdueByDays">
/// Whole days past the due date (<c>&gt;= 1</c>) when the task is overdue, else <c>null</c>. Computed
/// server-side because the frontend's <c>taskDueDate.isOverdue</c> compares by <b>calendar day</b>,
/// and two different definitions on one screen would look like a bug.
/// </param>
public sealed record DashboardTaskItem(
    Guid Id,
    Guid BoardId,
    Guid ColumnId,
    string Title,
    string BoardName,
    string ColumnName,
    DateTimeOffset? DueDate,
    string? Priority,
    DateTimeOffset CreatedAt,
    Guid? AssigneeId,
    bool IsDone,
    int? OverdueByDays);

/// <summary>
/// One bucket of tasks. <see cref="Count"/> is the <b>total</b> matching the bucket, not
/// <c>Items.Count</c>: the tab header must show the real number even when the list is truncated by
/// <c>take</c>.
/// </summary>
public sealed record DashboardTaskBucket(int Count, IReadOnlyList<DashboardTaskItem> Items);

/// <summary>Per-column task count of one board (the "tóm tắt board" breakdown).</summary>
public sealed record DashboardBoardColumnCount(Guid ColumnId, string Name, bool IsDone, int Count);

/// <summary>One board's task summary. Counts use the shared <c>isDone</c> definition.</summary>
public sealed record DashboardBoardSummary(
    Guid BoardId,
    string Name,
    int Total,
    int Done,
    int Open,
    int Overdue,
    IReadOnlyList<DashboardBoardColumnCount> Columns);

/// <summary>
/// One workspace activity event for the dashboard feed.
/// <para>
/// <see cref="Payload"/> is always <c>null</c> here (Phase 12 §P5): the raw jsonb is whatever the
/// writer stored, and a dashboard reads like a headline list — the full diff stays behind the
/// Manager-only activity page.
/// </para>
/// </summary>
public sealed record DashboardActivityItem(
    Guid Id,
    Guid? BoardId,
    string EntityType,
    string Action,
    string? AuthorName,
    DateTimeOffset CreatedAt,
    object? Payload);

/// <summary>Workspace-wide totals for the dashboard header.</summary>
public sealed record DashboardSummary(
    int TotalTasks,
    int DoneTasks,
    int OpenTasks,
    int OverdueTasks,
    int MyOpenTasks);

/// <summary>The three slices of "Task của tôi". They are independent: one task may appear in more than one.</summary>
public sealed record DashboardMyTasks(
    DashboardTaskBucket Overdue,
    DashboardTaskBucket DueSoon,
    DashboardTaskBucket RecentlyAssigned);

/// <summary>
/// "Sức khỏe dự án" of one workspace (Phase 14 §3.2), as the dashboard renders it.
/// <para>
/// <see cref="Components"/> holds the <b>points deducted</b> per risk factor
/// (<c>overdue</c>/<c>atRisk</c>/<c>stalled</c>/<c>aging</c>/<c>load</c>), which is what makes the
/// gauge's tooltip explain itself: the components plus <see cref="Score"/> add up to 100.
/// </para>
/// <para>
/// <see cref="Band"/> is the band, computed server-side (<c>Tốt</c>/<c>Cần chú ý</c>/<c>Rủi ro</c>/
/// <c>Nghiêm trọng</c>), so the UI only has to colour by band and can never disagree with the number.
/// <see cref="Reasons"/> is a short Vietnamese explanation list, empty for a healthy workspace.
/// </para>
/// </summary>
public sealed record DashboardProjectHealth(
    int Score,
    string Band,
    IReadOnlyDictionary<string, double> Components,
    IReadOnlyList<string> Reasons)
{
    /// <summary>
    /// One-way projection from the domain verdict, in the style of
    /// <c>ReportSummaryResponse.From</c>: the HTTP contract can evolve without touching the formula,
    /// and <c>DashboardService</c> never has to know the wire shape.
    /// </summary>
    public static DashboardProjectHealth From(Services.ProjectHealthResult result)
        => new(result.Score, result.Band, result.Components, result.Reasons);
}

/// <summary>
/// Payload of <c>GET /api/workspaces/{workspaceId}/dashboard</c> (Phase 12 §1, decision D5).
/// </summary>
/// <param name="UtcNow">The instant the server computed the buckets from — lets the UI label the data without guessing.</param>
/// <param name="DueSoonDays">The active "sắp đến hạn" window in days (echoes the clamped <c>?days=</c>).</param>
/// <param name="BoardsTruncated">True when the workspace has more boards than the response includes.</param>
/// <param name="Health">
/// Phase 14 §3.2 — a <b>property appended last</b> (not a new positional parameter) on purpose
/// (decision D15): every existing construction site and every existing client keeps compiling and
/// deserializing the nine fields it already knew, and a client that predates the field simply ignores
/// it. Non-null for every workspace (an empty workspace is a healthy one, score 100) — nullable so a
/// future "not computed" state does not require a breaking change.
/// </param>
public sealed record DashboardResponse(
    Guid WorkspaceId,
    string WorkspaceName,
    DateTimeOffset UtcNow,
    int DueSoonDays,
    DashboardMyTasks MyTasks,
    IReadOnlyList<DashboardBoardSummary> Boards,
    bool BoardsTruncated,
    IReadOnlyList<DashboardActivityItem> RecentActivities,
    DashboardSummary Summary)
{
    /// <summary>"Sức khỏe dự án" (Phase 14 §3.2). <c>null</c> ⇒ not computed.</summary>
    public DashboardProjectHealth? Health { get; init; }
}
