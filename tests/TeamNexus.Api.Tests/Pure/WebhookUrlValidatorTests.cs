using TeamNexus.Modules.Ai.Services.Integrations;
using TeamNexus.Modules.Board.Services;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>Phase 15.2 §T15.2.5 — the SSRF guard for outbound webhook URLs. Pure: no DNS, no network.</summary>
public sealed class WebhookUrlValidatorTests
{
    private const string Slack = "https://hooks.slack.com/services/T0001/B0002/abcdefghijkl";
    private const string Discord = "https://discord.com/api/webhooks/123456789/tokenABCDEFGH";

    [Fact]
    public void ValidSlackUrl_IsAccepted()
    {
        var (provider, url) = WebhookUrlValidator.Validate("Slack", Slack);
        Assert.Equal("slack", provider);
        Assert.Equal(Slack, url);
    }

    [Theory]
    [InlineData("https://discord.com/api/webhooks/1/abc")]
    [InlineData("https://discordapp.com/api/webhooks/1/abc")]
    [InlineData("https://ptb.discord.com/api/webhooks/1/abc")]
    public void ValidDiscordUrls_AreAccepted(string raw)
    {
        var (provider, _) = WebhookUrlValidator.Validate("discord", raw);
        Assert.Equal("discord", provider);
    }

    [Theory]
    [InlineData("http://hooks.slack.com/services/T/B/x")]            // not https
    [InlineData("https://user:pw@hooks.slack.com/services/T/B/x")]   // user-info
    [InlineData("https://hooks.slack.com:8443/services/T/B/x")]      // non-default port
    [InlineData("https://localhost/services/T/B/x")]
    [InlineData("https://127.0.0.1/services/T/B/x")]
    [InlineData("https://10.0.0.1/services/T/B/x")]
    [InlineData("https://172.16.0.1/services/T/B/x")]
    [InlineData("https://192.168.1.1/services/T/B/x")]
    [InlineData("https://169.254.169.254/services/T/B/x")]
    [InlineData("https://[::1]/services/T/B/x")]
    [InlineData("https://evil.example.com/services/T/B/x")]          // wrong host
    [InlineData("https://hooks.slack.com.evil.com/services/T/B/x")]  // look-alike host
    [InlineData("https://hooks.slack.com/api/webhooks/T/B/x")]       // wrong path prefix
    [InlineData("https://hooks.slack.com/services/T")]               // too few segments
    [InlineData("not a url")]
    [InlineData("")]
    public void UnsafeSlackUrls_AreRejected(string raw)
        => Assert.Throws<BadRequestException>(() => WebhookUrlValidator.Validate("slack", raw));

    [Theory]
    [InlineData("https://hooks.slack.com/services/T/B/x")]           // Slack URL on the Discord provider
    [InlineData("https://discord.com/services/1/abc")]               // wrong path prefix
    [InlineData("https://discord.com/api/webhooks/1")]               // missing token
    [InlineData("https://notdiscord.com/api/webhooks/1/abc")]
    public void UnsafeDiscordUrls_AreRejected(string raw)
        => Assert.Throws<BadRequestException>(() => WebhookUrlValidator.Validate("discord", raw));

    [Fact]
    public void OverlongUrl_IsRejected()
        => Assert.Throws<BadRequestException>(() => WebhookUrlValidator.Validate(
            "slack", "https://hooks.slack.com/services/T/B/" + new string('x', 1100)));

    [Theory]
    [InlineData("teams")]
    [InlineData("")]
    [InlineData(null)]
    public void UnknownProvider_IsRejected(string? provider)
        => Assert.Throws<BadRequestException>(() => WebhookUrlValidator.Validate(provider, Slack));

    [Fact]
    public void MaskUrl_HidesTheSecretTail_ButKeepsLastFour()
    {
        var masked = WebhookUrlValidator.MaskUrl(Slack);
        Assert.EndsWith("****ijkl", masked);
        Assert.DoesNotContain("abcdefgh", masked);
    }
}

