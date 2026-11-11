using System.Text.Json;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Builds Slack Block Kit JSON payloads for outbound workspace notifications (Phase 15.2 §T15.2.2).
/// </summary>
public static class SlackBlockKitFormatter
{
    public const int MaxHeaderLength = 150;
    public const int MaxSectionTextLength = 2800;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Format(ExternalAlertMessage message, string? frontendBaseUrl = null)
    {
        var isAlert = WebhookEventMatcher.IsHighSeverityEvent(message.Type, message.PayloadJson);
        var emoji = isAlert ? "🚨" : "🔔";
        var eventLabel = WebhookEventMatcher.GetVietnameseEventLabel(message.Type);
        var rawHeader = $"{emoji} {message.Title}";
        var headerText = Truncate(rawHeader, MaxHeaderLength);
        var bodyText = Truncate(message.Message, MaxSectionTextLength);
        var deepLink = BuildDeepLink(message.WorkspaceId, frontendBaseUrl);

        var fallbackText = Truncate($"{emoji} [{eventLabel}] {message.Title}: {message.Message}", 500);

        var payload = new
        {
            text = fallbackText,
            blocks = new object[]
            {
                new
                {
                    type = "header",
                    text = new
                    {
                        type = "plain_text",
                        text = headerText,
                        emoji = true,
                    },
                },
                new
                {
                    type = "section",
                    text = new
                    {
                        type = "mrkdwn",
                        text = bodyText,
                    },
                    fields = new object[]
                    {
                        new
                        {
                            type = "mrkdwn",
                            text = $"*Loại sự kiện:*\n`{eventLabel}`",
                        },
                        new
                        {
                            type = "mrkdwn",
                            text = $"*Không gian làm việc:*\n`#{message.WorkspaceId.ToString("N")[..8].ToUpperInvariant()}`",
                        },
                    },
                },
                new
                {
                    type = "actions",
                    elements = new object[]
                    {
                        new
                        {
                            type = "button",
                            text = new
                            {
                                type = "plain_text",
                                text = "Mở trong TeamNexus",
                                emoji = true,
                            },
                            url = deepLink,
                            style = isAlert ? "danger" : "primary",
                        },
                    },
                },
            },
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string BuildDeepLink(Guid workspaceId, string? frontendBaseUrl)
    {
        var baseUrl = string.IsNullOrWhiteSpace(frontendBaseUrl)
            ? "https://app.teamnexus.cloud"
            : frontendBaseUrl.Trim().TrimEnd('/');

        return $"{baseUrl}/workspaces/{workspaceId}/boards";
    }

    private static string Truncate(string? value, int maxLength)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "(Không có nội dung)" : value.Trim();
        if (text.Length <= maxLength)
        {
            return text;
        }

        return $"{text[..(maxLength - 1)]}…";
    }
}

