using System.Text.Json;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Builds Discord Rich Embed JSON payloads for outbound workspace notifications (Phase 15.2 §T15.2.2).
/// </summary>
public static class DiscordEmbedFormatter
{
    public const int ColorDangerRed = 15548997;     // #ED4245
    public const int ColorRoyalIndigo = 5195493;    // #4F46E5
    public const int MaxTitleLength = 256;
    public const int MaxDescriptionLength = 4096;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string Format(ExternalAlertMessage message, string? frontendBaseUrl = null, DateTimeOffset? timestamp = null)
    {
        var isAlert = WebhookEventMatcher.IsHighSeverityEvent(message.Type, message.PayloadJson);
        var color = isAlert ? ColorDangerRed : ColorRoyalIndigo;
        var eventLabel = WebhookEventMatcher.GetVietnameseEventLabel(message.Type);
        var deepLink = SlackBlockKitFormatter.BuildDeepLink(message.WorkspaceId, frontendBaseUrl);
        var utcTimestamp = (timestamp ?? DateTimeOffset.UtcNow).ToUniversalTime().ToString("O");

        var payload = new
        {
            username = "TeamNexus AI",
            embeds = new object[]
            {
                new
                {
                    title = Truncate(message.Title, MaxTitleLength),
                    description = Truncate(message.Message, MaxDescriptionLength),
                    url = deepLink,
                    color,
                    fields = new object[]
                    {
                        new
                        {
                            name = "Loại sự kiện",
                            value = eventLabel,
                            inline = true,
                        },
                        new
                        {
                            name = "Không gian làm việc",
                            value = $"#{message.WorkspaceId.ToString("N")[..8].ToUpperInvariant()}",
                            inline = true,
                        },
                    },
                    footer = new
                    {
                        text = "TeamNexus Hybrid AI Workspace",
                    },
                    timestamp = utcTimestamp,
                },
            },
        };

        return JsonSerializer.Serialize(payload, JsonOptions);
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

