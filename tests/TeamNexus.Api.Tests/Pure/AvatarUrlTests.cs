using TeamNexus.Shared.Profile;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 11 §5.1 — the avatar-URL rule. Pure and cheap, and it is the only thing standing between a
/// user-supplied string and an <c>&lt;img src&gt;</c> on every page that renders them, so the
/// dangerous schemes get their own cases.
/// </summary>
public sealed class AvatarUrlTests
{
    [Theory]
    [InlineData("https://cdn.example.com/a.png")]
    [InlineData("http://example.com/a.png")]
    [InlineData("HTTPS://EXAMPLE.COM/a.png")]
    [InlineData("https://gravatar.com/avatar/abc123?s=200&d=identicon")]
    public void IsSafe_AcceptsAbsoluteHttpAndHttpsUrls(string url)
    {
        Assert.True(AvatarUrl.IsSafe(url));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsSafe_TreatsNoAvatarAsValid(string? url)
    {
        Assert.True(AvatarUrl.IsSafe(url));
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("JavaScript:alert(1)")]
    [InlineData("  javascript:alert(1)  ")]
    [InlineData("data:text/html;base64,PHNjcmlwdD4=")]
    [InlineData("file:///C:/secrets.txt")]
    [InlineData("vbscript:msgbox(1)")]
    [InlineData("ftp://example.com/a.png")]
    public void IsSafe_RejectsDangerousSchemes(string url)
    {
        Assert.False(AvatarUrl.IsSafe(url));
    }

    [Theory]
    [InlineData("//evil.example.com/a.png")]
    [InlineData("/relative/a.png")]
    [InlineData("cdn.example.com/a.png")]
    [InlineData("https://")]
    public void IsSafe_RejectsNonAbsoluteOrHostlessValues(string url)
    {
        Assert.False(AvatarUrl.IsSafe(url));
    }

    [Fact]
    public void IsSafe_RejectsAUrlLongerThanTheLimit()
    {
        var url = "https://cdn.example.com/" + new string('x', AvatarUrl.MaxLength);

        Assert.False(AvatarUrl.IsSafe(url));
    }

    [Fact]
    public void IsSafe_AcceptsAUrlExactlyAtTheLimit()
    {
        var prefix = "https://cdn.example.com/";
        var url = prefix + new string('x', AvatarUrl.MaxLength - prefix.Length);

        Assert.Equal(AvatarUrl.MaxLength, url.Length);
        Assert.True(AvatarUrl.IsSafe(url));
    }

    [Fact]
    public void Normalize_MapsBlankToNull()
    {
        Assert.Null(AvatarUrl.Normalize(null));
        Assert.Null(AvatarUrl.Normalize(""));
        Assert.Null(AvatarUrl.Normalize("   "));
    }

    [Fact]
    public void Normalize_TrimsButPreservesTheUrl()
    {
        Assert.Equal(
            "https://cdn.example.com/A.png",
            AvatarUrl.Normalize("  https://cdn.example.com/A.png  "));
    }
}
