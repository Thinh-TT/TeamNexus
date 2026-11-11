using TeamNexus.Modules.Ai.Services.Agent;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Canonical outbound webhook event catalog and subscription matcher (Phase 15.2 §T15.2.3).
/// Maps fine-grained in-app <c>notifications.type</c> values onto the workspace webhook subscription categories.
/// </summary>
public static class WebhookEventMatcher
{
    public const string ObserverAlert = "ObserverAlert";
    public const string AgentActionPending = "AgentActionPending";
    public const string ClarificationNeeded = "ClarificationNeeded";
    public const string TaskAssigned = "TaskAssigned";
    public const string CommentMentioned = "CommentMentioned";
    public const string MemberJoined = "MemberJoined";
    public const string TestPing = "TestPing";

    public const string DefaultSubscribedEventsCsv = "ObserverAlert,AgentActionPending,ClarificationNeeded";

    public static readonly IReadOnlyList<string> DefaultSubscribedEvents =
    [
        ObserverAlert,
        AgentActionPending,
        ClarificationNeeded,
    ];

    public static readonly HashSet<string> SupportedSubscriptionKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        ObserverAlert,
        "ObserverFinding",
        AgentActionPending,
        "AgentRunFailed",
        ClarificationNeeded,
        "AgentAwaiting",
        TaskAssigned,
        CommentMentioned,
        Board.Services.MemberNotificationTypes.CommentMention,
        Board.Services.MemberNotificationTypes.CommentOnTask,
        MemberJoined,
        NotificationTypes.OverdueTask,
        NotificationTypes.StalledTask,
        NotificationTypes.AtRiskDeadline,
        NotificationTypes.Overload,
        NotificationTypes.Bottleneck,
        AgentNotificationTypes.OutputPending,
        AgentNotificationTypes.RecoveryPlanPending,
        AgentNotificationTypes.AwaitingClarification,
    };

    /// <summary>
    /// Maps an internal <c>Notification.Type</c> to its primary canonical webhook category.
    /// </summary>
    public static string MapToCanonicalCategory(string? notificationType)
    {
        if (string.IsNullOrWhiteSpace(notificationType))
        {
            return string.Empty;
        }

        var trimmed = notificationType.Trim();

        if (string.Equals(trimmed, ObserverAlert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "ObserverFinding", StringComparison.OrdinalIgnoreCase)
            || NotificationTypes.IsKnown(trimmed))
        {
            return ObserverAlert;
        }

        if (string.Equals(trimmed, ClarificationNeeded, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "AgentAwaiting", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, AgentNotificationTypes.AwaitingClarification, StringComparison.OrdinalIgnoreCase))
        {
            return ClarificationNeeded;
        }

        if (string.Equals(trimmed, AgentActionPending, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, AgentNotificationTypes.OutputPending, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, AgentNotificationTypes.RecoveryPlanPending, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, AgentNotificationTypes.RunFailed, StringComparison.OrdinalIgnoreCase))
        {
            return AgentActionPending;
        }

        if (string.Equals(trimmed, Board.Services.MemberNotificationTypes.TaskAssigned, StringComparison.OrdinalIgnoreCase))
        {
            return TaskAssigned;
        }

        if (string.Equals(trimmed, CommentMentioned, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, Board.Services.MemberNotificationTypes.CommentMention, StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, Board.Services.MemberNotificationTypes.CommentOnTask, StringComparison.OrdinalIgnoreCase))
        {
            return CommentMentioned;
        }

        if (string.Equals(trimmed, Board.Services.MemberNotificationTypes.MemberJoined, StringComparison.OrdinalIgnoreCase))
        {
            return MemberJoined;
        }

        return trimmed;
    }

    /// <summary>
    /// Determines whether an endpoint's comma-separated <paramref name="subscribedEventsCsv"/> matches
    /// the given <paramref name="notificationType"/>.
    /// </summary>
    public static bool IsSubscribed(string? subscribedEventsCsv, string? notificationType)
    {
        if (string.IsNullOrWhiteSpace(subscribedEventsCsv) || string.IsNullOrWhiteSpace(notificationType))
        {
            return false;
        }

        var tokens = ParseCsv(subscribedEventsCsv);
        if (tokens.Count == 0)
        {
            return false;
        }

        var rawType = notificationType.Trim();
        var canonical = MapToCanonicalCategory(rawType);

        foreach (var token in tokens)
        {
            if (string.Equals(token, rawType, StringComparison.OrdinalIgnoreCase)
                || string.Equals(token, canonical, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Legacy / alias compatibility ("ObserverFinding" <-> "ObserverAlert", "CommentMention" <-> "CommentMentioned")
            if (string.Equals(MapToCanonicalCategory(token), canonical, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<string> ParseCsv(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return [];
        }

        return csv
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeAndValidateEvents(IReadOnlyList<string>? events)
    {
        if (events is null || events.Count == 0)
        {
            return DefaultSubscribedEventsCsv;
        }

        var cleaned = new List<string>();
        foreach (var item in events)
        {
            foreach (var part in (item ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!SupportedSubscriptionKeys.Contains(part))
                {
                    throw new BadRequestException($"Loại sự kiện đăng ký '{part}' không hợp lệ.");
                }

                if (!cleaned.Contains(part, StringComparer.OrdinalIgnoreCase))
                {
                    cleaned.Add(part);
                }
            }
        }

        return cleaned.Count == 0
            ? DefaultSubscribedEventsCsv
            : string.Join(",", cleaned);
    }

    public static string GetVietnameseEventLabel(string? type)
    {
        var canonical = MapToCanonicalCategory(type);
        return canonical switch
        {
            ObserverAlert => "Cảnh báo rủi ro AI Observer",
            AgentActionPending => "Đề xuất / Cảnh báo AI Agent",
            ClarificationNeeded => "Thẻ cần làm rõ",
            TaskAssigned => "Giao việc mới",
            CommentMentioned => "Nhắc tên / Bình luận công việc",
            MemberJoined => "Thành viên mới tham gia",
            TestPing => "Kiểm tra kết nối Webhook",
            _ => type ?? "Thông báo hệ thống",
        };
    }

    public static bool IsHighSeverityEvent(string? type, string? payloadJson = null)
    {
        if (string.IsNullOrWhiteSpace(type))
        {
            return false;
        }

        var canonical = MapToCanonicalCategory(type);
        if (string.Equals(canonical, ObserverAlert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(type, AgentNotificationTypes.RunFailed, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(payloadJson)
            && payloadJson.Contains("\"Critical\"", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }
}

