using System.Text.Json;
using TeamNexus.Modules.Ai.Services.Integrations;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>Phase 15.2 §T15.2.5 — Slack/Discord payload shape and the subscription matcher.</summary>
public sealed class WebhookPayloadFormatterTests
{
    private static readonly Guid Workspace = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static ExternalAlertMessage Message(string type = "OverdueTask", string title = "Tiêu đề", string body = "Nội dung")
        => new(Workspace, type, title, body, null, [Guid.NewGuid()]);

    [Fact]
    public void Slack_HasFallbackTextHeaderSectionAndDeepLinkButton()
    {
        using var doc = JsonDocument.Parse(SlackBlockKitFormatter.Format(Message(), "https://app.example.test"));
        var root = doc.RootElement;

        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("text").GetString()));
        var blocks = root.GetProperty("blocks");
        Assert.Equal(["header", "section", "actions"], blocks.EnumerateArray().Select(b => b.GetProperty("type").GetString()));

        var url = blocks[2].GetProperty("elements")[0].GetProperty("url").GetString();
        Assert.StartsWith($"https://app.example.test/workspaces/{Workspace}", url);
    }

    [Fact]
    public void Slack_HeaderIsTruncatedTo150Characters()
    {
        using var doc = JsonDocument.Parse(SlackBlockKitFormatter.Format(Message(title: new string('a', 500))));
        var header = doc.RootElement.GetProperty("blocks")[0].GetProperty("text").GetProperty("text").GetString()!;
        Assert.True(header.Length <= SlackBlockKitFormatter.MaxHeaderLength);
    }

    [Fact]
    public void Discord_HasUsernameEmbedFooterAndUtcTimestamp()
    {
        using var doc = JsonDocument.Parse(DiscordEmbedFormatter.Format(
            Message(), "https://app.example.test", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero)));
        var root = doc.RootElement;

        Assert.Equal("TeamNexus AI", root.GetProperty("username").GetString());
        var embed = root.GetProperty("embeds")[0];
        Assert.Equal("TeamNexus Hybrid AI Workspace", embed.GetProperty("footer").GetProperty("text").GetString());
        Assert.StartsWith("2026-01-02T03:04:05", embed.GetProperty("timestamp").GetString());
    }

    [Fact]
    public void Discord_LimitsTitleAndDescription()
    {
        using var doc = JsonDocument.Parse(DiscordEmbedFormatter.Format(
            Message(title: new string('t', 1000), body: new string('d', 9000))));
        var embed = doc.RootElement.GetProperty("embeds")[0];
        Assert.True(embed.GetProperty("title").GetString()!.Length <= 256);
        Assert.True(embed.GetProperty("description").GetString()!.Length <= 4096);
    }

    [Fact]
    public void Discord_UsesRedForObserverAlerts_AndIndigoOtherwise()
    {
        static int Color(string type)
        {
            using var doc = JsonDocument.Parse(DiscordEmbedFormatter.Format(
                new ExternalAlertMessage(Workspace, type, "t", "m", null, [])));
            return doc.RootElement.GetProperty("embeds")[0].GetProperty("color").GetInt32();
        }

        Assert.Equal(DiscordEmbedFormatter.ColorDangerRed, Color("OverdueTask"));
        Assert.Equal(DiscordEmbedFormatter.ColorRoyalIndigo, Color("TaskAssigned"));
    }

    [Theory]
    [InlineData("ObserverAlert", "OverdueTask", true)]
    [InlineData("ObserverAlert", "Bottleneck", true)]
    [InlineData("ObserverAlert", "TaskAssigned", false)]
    [InlineData("TaskAssigned", "TaskAssigned", true)]
    [InlineData("MemberJoined", "MemberJoined", true)]
    [InlineData("CommentMentioned", "CommentMention", true)]
    [InlineData("CommentMentioned", "CommentOnTask", true)]
    [InlineData("ClarificationNeeded", "AgentAwaitingClarification", true)]
    [InlineData("OverdueTask", "OverdueTask", true)]              // fine-grained token
    [InlineData("MemberJoined", "OverdueTask", false)]
    [InlineData("", "OverdueTask", false)]
    public void Matcher_RoutesNotificationTypesToSubscriptions(string subscribed, string type, bool expected)
        => Assert.Equal(expected, WebhookEventMatcher.IsSubscribed(subscribed, type));
}

