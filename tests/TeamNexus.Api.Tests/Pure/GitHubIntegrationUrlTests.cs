using Microsoft.Extensions.Configuration;
using TeamNexus.Modules.Ai.Services.Integrations;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 15.3 review F2: the GitHub Payload URL must never be built from the SPA origin by accident.
/// The production SPA rewrites every path to <c>index.html</c>, so a wrong host here is not a cosmetic
/// bug — GitHub's first delivery silently never reaches the API. That is why the resolution order is
/// pinned by tests instead of being an implementation detail.
/// </summary>
public sealed class GitHubIntegrationUrlTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values)
    {
        var pairs = values.ToDictionary(
            pair => pair.Key,
            pair => pair.Value,
            StringComparer.OrdinalIgnoreCase);

        return new ConfigurationBuilder().AddInMemoryCollection(pairs).Build();
    }

    [Fact]
    public void ResolvePublicApiBaseUrl_UsesPublicApiBaseUrl_First()
    {
        var configuration = Config(
            ("Integrations:PublicApiBaseUrl", "https://api.teamnexus.cloud/"),
            ("Integrations:ApiBaseUrl", "https://legacy.example"),
            ("Frontend:BaseUrl", "https://app.teamnexus.cloud"));

        Assert.Equal("https://api.teamnexus.cloud", WorkspaceGitHubIntegrationService.ResolvePublicApiBaseUrl(configuration));
    }

    [Fact]
    public void ResolvePublicApiBaseUrl_FallsBackToLegacyApiBaseUrl()
    {
        var configuration = Config(
            ("Integrations:ApiBaseUrl", "https://legacy.example/"),
            ("Frontend:BaseUrl", "https://app.teamnexus.cloud"));

        Assert.Equal("https://legacy.example", WorkspaceGitHubIntegrationService.ResolvePublicApiBaseUrl(configuration));
    }

    [Fact]
    public void ResolvePublicApiBaseUrl_FallsBackToFrontendBaseUrl_ForLocalDevProxy()
    {
        var configuration = Config(("Frontend:BaseUrl", "http://localhost:5173"));

        Assert.Equal("http://localhost:5173", WorkspaceGitHubIntegrationService.ResolvePublicApiBaseUrl(configuration));
    }

    [Fact]
    public void ResolvePublicApiBaseUrl_UsesProductionDefault_WhenNothingConfigured()
    {
        var configuration = Config();

        Assert.Equal("https://api.teamnexus.cloud", WorkspaceGitHubIntegrationService.ResolvePublicApiBaseUrl(configuration));
    }

    [Fact]
    public void ResolvePublicApiBaseUrl_IgnoresBlankOverrides()
    {
        // appsettings.Development.json deliberately sets PublicApiBaseUrl to "" so dev traffic goes
        // through the Vite /api proxy; a blank value must never win over the next candidate.
        var configuration = Config(
            ("Integrations:PublicApiBaseUrl", "   "),
            ("Frontend:BaseUrl", "http://localhost:5173"));

        Assert.Equal("http://localhost:5173", WorkspaceGitHubIntegrationService.ResolvePublicApiBaseUrl(configuration));
    }

    [Fact]
    public void BuildPayloadUrl_AppendsGitHubWebhookRoute()
    {
        var token = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var configuration = Config(("Integrations:PublicApiBaseUrl", "https://api.teamnexus.cloud"));

        var payloadUrl = WorkspaceGitHubIntegrationService.BuildPayloadUrl(configuration, token);

        Assert.Equal($"https://api.teamnexus.cloud/api/webhooks/github/{token:D}", payloadUrl);
        Assert.DoesNotContain("app.teamnexus.cloud", payloadUrl, StringComparison.OrdinalIgnoreCase);
    }
}
