namespace TeamNexus.Modules.Ai.Services;

using TeamNexus.Modules.Ai.Services.Agent;

/// <summary>
/// The three alert families that share the <c>notifications</c> table, as a filterable dimension
/// (Phase 12 §3.4, decision D4).
/// <para>
/// The vocabulary was already split three ways in code, for a reason worth restating: the Observer's
/// <see cref="NotificationTypes.All"/> is an <b>anti-hallucination whitelist</b> (the model may only
/// echo those four types back), <c>AgentNotificationTypes</c> belong to the executor, and the
/// member-facing types live on the Board side. The dashboard's "cảnh báo AI Observer chưa đọc" tile
/// must not count a "bạn được giao thẻ" alert, and the only way to answer that correctly is to filter
/// <b>in the query</b> — the badge count has to be computed over the same rows as the list, or the bell
/// would show a number the list can never explain.
/// </para>
/// <para>
/// Nothing is written here and no type is added to <see cref="NotificationTypes.All"/>: that list must
/// stay the Observer's four signals (Phase 11 D10).
/// </para>
/// </summary>
public static class NotificationVocabulary
{
    /// <summary>Query-string values accepted by <c>GET /api/notifications?kind=</c>.</summary>
    public const string Observer = "observer";

    public const string Agent = "agent";

    public const string Member = "member";

    /// <summary>The Observer's own signals — exactly <see cref="NotificationTypes.All"/>.</summary>
    public static IReadOnlyList<string> ObserverTypes => NotificationTypes.All;

    /// <summary>The executor's three alerts, as persisted.</summary>
    public static IReadOnlyList<string> AgentTypes { get; } =
        [AgentNotificationTypes.RunFailed, AgentNotificationTypes.AwaitingClarification, AgentNotificationTypes.OutputPending];

    /// <summary>
    /// The member-facing alerts. Kept as an explicit list rather than discovered by reflection: an
    /// unknown value here would silently stop matching rows, and the frontend's label map is the other
    /// half of the same contract.
    /// </summary>
    public static IReadOnlyList<string> MemberTypes { get; } =
    [
        Board.Services.MemberNotificationTypes.TaskAssigned,
        Board.Services.MemberNotificationTypes.CommentOnTask,
        Board.Services.MemberNotificationTypes.CommentMention,
        Board.Services.MemberNotificationTypes.WorkspaceInvitation,
    ];

    /// <summary>
    /// Parses the <c>kind</c> query value into the type list to filter on, or <c>null</c> for "every
    /// kind" (the parameter was absent). An unknown value is a 400 rather than an empty page — a typo
    /// must not look like "you have no notifications".
    /// </summary>
    public static IReadOnlyList<string>? Parse(string? kind)
    {
        var value = kind?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        return value.ToLowerInvariant() switch
        {
            Observer => ObserverTypes,
            Agent => AgentTypes,
            Member => MemberTypes,
            _ => throw new Board.Services.BadRequestException(
                $"Unknown value for kind '{value}'. Expected one of: {Observer}, {Agent}, {Member}."),
        };
    }
}
