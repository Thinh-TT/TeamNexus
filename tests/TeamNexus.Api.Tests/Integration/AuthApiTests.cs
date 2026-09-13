using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using TeamNexus.Api.Tests.Infrastructure;
using TeamNexus.Modules.Auth;
using TeamNexus.Modules.Auth.Services;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Integration;

/// <summary>
/// Phase 8 §2.4 — the authentication contract: policy enforcement, token validation, anti-CSRF,
/// refresh-token rotation (including replay defence) and logout revocation.
/// <para>
/// The CSRF suites are the ones that would have caught the deploy-blocking cookie problems found
/// while writing §3, which is exactly why they are priority 2 rather than "nice to have".
/// </para>
/// </summary>
public sealed class AuthApiTests : IClassFixture<DatabaseFixture>
{
    private readonly DatabaseFixture _database;

    public AuthApiTests(DatabaseFixture database)
    {
        _database = database;
    }

    // ---- health & anonymous access -----------------------------------------

    [Fact]
    public async Task Health_IsReachableWithoutAuthentication()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var client = await scenario.AnonymousAsync();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.Equal("ok", body?.Status);
        Assert.Equal("TeamNexus.Api", body?.Service);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutAToken_Returns401()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var client = await scenario.AnonymousAsync();

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- token validation ---------------------------------------------------

    [Fact]
    public async Task Me_WithAValidToken_ReturnsTheUserAndItsRoles()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Nguyễn Văn A", "a@example.test", role: AuthConstants.DefaultUserRole);

        using var client = await scenario.AsUserAsync(user, AuthConstants.DefaultUserRole);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal(user.Id, body?.Id);
        Assert.Equal("Nguyễn Văn A", body?.DisplayName);
        Assert.Equal("a@example.test", body?.Email);
        Assert.Contains(AuthConstants.DefaultUserRole, body!.Roles);
    }

    [Fact]
    public async Task Me_WithATokenSignedByTheWrongKey_Returns401()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Kẻ giả mạo");

        using var client = scenario.WithTamperedToken(user);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithAnExpiredToken_Returns401()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Token hết hạn");

        using var client = scenario.WithExpiredToken(user);
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithAGarbageBearerValue_Returns401()
    {
        await using var scenario = await _database.CreateScenarioAsync();

        using var client = await scenario.AnonymousAsync();
        client.Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- role-based authorization ------------------------------------------

    [Fact]
    public async Task AdminPing_WithTheAdminRole_IsAllowed()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var admin = await scenario.CreateUserAsync("Quản trị", role: "Admin");

        using var client = await scenario.AsUserAsync(admin, "Admin");
        var response = await client.GetAsync("/api/admin/ping");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AdminPing_WithTheUserRole_IsForbidden()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Thành viên", role: AuthConstants.DefaultUserRole);

        using var client = await scenario.AsUserAsync(user, AuthConstants.DefaultUserRole);
        var response = await client.GetAsync("/api/admin/ping");

        // 403 (authenticated but not authorized) — NOT 401, which would mean the token was rejected.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- anti-CSRF ----------------------------------------------------------

    [Fact]
    public async Task Refresh_WithoutTheXsrfHeader_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Chống CSRF");

        // A real session (cookies present), but the X-XSRF-TOKEN header is deliberately withheld —
        // the mirror image of httpClient.ts, which always echoes the cookie value.
        using var client = await scenario.SignedInAsync(user);
        client.RemoveAntiforgeryHeader();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithAnXsrfHeaderThatDoesNotMatchTheCookie_IsRejected()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("CSRF sai header");

        using var client = await scenario.SignedInAsync(user);
        client.RemoveAntiforgeryHeader();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/refresh");
        request.Headers.Add(AuthConstants.XsrfRequestHeader, "not-the-real-token");

        var response = await client.Http.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Antiforgery_Endpoint_IsReachableAnonymouslyAndSetsTheCookie()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var client = await scenario.AnonymousAsync();

        var response = await client.Http.GetAsync("/api/auth/antiforgery");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join("; ", values)
            : string.Empty;

        Assert.Contains(AuthConstants.XsrfTokenCookie, setCookie, StringComparison.Ordinal);
    }

    // ---- OAuth providers are opt-in ----------------------------------------

    [Theory]
    [InlineData("google")]
    [InlineData("github")]
    public async Task Login_ForAProviderWithoutCredentials_Returns400NotA500(string provider)
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var client = await scenario.AnonymousAsync();

        var response = await client.GetAsync($"/api/auth/login/{provider}");

        // AddAuthModule registers a provider only when its credentials exist (app boots with a
        // subset), so an unconfigured provider is a client error — never an unhandled exception.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_ForAnUnknownProvider_Returns404()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var client = await scenario.AnonymousAsync();

        var response = await client.GetAsync("/api/auth/login/myspace");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- refresh rotation ---------------------------------------------------

    [Fact]
    public async Task Refresh_RotatesTheTokenAndKeepsTheUserSignedIn()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Xoay token");

        var pair = await scenario.IssueTokenPairAsync(user);
        using var client = await scenario.SignedInAsync(pair);

        var response = await scenario.RefreshAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The access token changed and the user is still recognized on the protected route.
        var me = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        // One token in, one replacement row out: the chain grows without leaking active tokens.
        Assert.Equal(2, await scenario.CountRefreshTokensAsync(user.Id));
        Assert.True(await scenario.IsRefreshTokenRevokedAsync(pair.RawRefreshToken));
    }

    [Fact]
    public async Task Refresh_WithAReplayedToken_Returns401AndRevokesTheWholeFamily()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Chống replay");

        var first = await scenario.IssueTokenPairAsync(user);

        // Legitimate rotation: `first` is revoked, a replacement token is issued into the session.
        using var firstClient = await scenario.SignedInAsync(first);
        var rotation = await scenario.RefreshAsync(firstClient);
        Assert.Equal(HttpStatusCode.NoContent, rotation.StatusCode);
        Assert.True(await scenario.IsRefreshTokenRevokedAsync(first.RawRefreshToken));

        // Replay the ORIGINAL token from a session that still carries it → 401 and family revoked.
        using var replayed = await scenario.SignedInAsync(first);
        var replayResponse = await scenario.RefreshAsync(replayed);
        Assert.Equal(HttpStatusCode.Unauthorized, replayResponse.StatusCode);

        // Defence in depth: the token that was legitimately issued a moment ago is dead too, so a
        // stolen replacement cannot be used after a replay was detected.
        using var thirdClient = await scenario.SignedInAsync(first);
        var secondAttempt = await scenario.RefreshAsync(thirdClient);
        Assert.Equal(HttpStatusCode.Unauthorized, secondAttempt.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithAnUnknownRefreshToken_Returns401()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var client = await scenario.AnonymousAsync();

        scenario.AddRefreshCookie("this-token-was-never-issued");

        var response = await scenario.RefreshAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_WithoutAnyRefreshCookie_Returns401()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var client = await scenario.AnonymousAsync();

        var response = await scenario.RefreshAsync(client);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- logout -------------------------------------------------------------

    [Fact]
    public async Task Logout_RevokesTheRefreshTokenSoItCannotBeUsedAgain()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Đăng xuất");

        var pair = await scenario.IssueTokenPairAsync(user);
        using var client = await scenario.SignedInAsync(pair);

        var logout = await scenario.LogoutAsync(client);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        Assert.True(await scenario.IsRefreshTokenRevokedAsync(pair.RawRefreshToken));
    }

    [Fact]
    public async Task Logout_IsIdempotent()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Đăng xuất hai lần");

        var pair = await scenario.IssueTokenPairAsync(user);
        using var client = await scenario.SignedInAsync(pair);

        var first = await scenario.LogoutAsync(client);
        var second = await scenario.LogoutAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
    }

    [Fact]
    public async Task Logout_WithNoSessionAtAll_StillReturns204()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        using var client = await scenario.AnonymousAsync();

        var response = await scenario.LogoutAsync(client);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ---- persisted token shape ---------------------------------------------

    [Fact]
    public async Task IssuedTokens_AreNonTrivialAndOnlyTheRefreshHashIsPersisted()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Hash refresh token");

        var pair = await scenario.IssueTokenPairAsync(user);

        Assert.False(string.IsNullOrWhiteSpace(pair.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(pair.RawRefreshToken));

        // Only the hash of the refresh token is ever persisted — never the raw value.
        var storedHash = await scenario.FindRefreshTokenHashAsync(pair.RawRefreshToken);
        Assert.NotNull(storedHash);
        Assert.NotEqual(pair.RawRefreshToken, storedHash);
        Assert.Equal(JwtService.HashToken(pair.RawRefreshToken), storedHash);
    }

    // ---- cookie transport (Phase 8 D7 — the split-deployment blocker) -------

    [Fact]
    public async Task AuthCookies_UseLaxByDefault()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Cookie SameSite mặc định");

        var setCookie = await RotateAndReadSetCookieAsync(
            new TeamNexusApiFactory { ConnectionString = TestScenario.ConnectionStringForTests }, user);

        // Local development keeps Lax: frontend and API share the localhost site through the Vite
        // proxy, and this is the documented default in appsettings.
        Assert.Contains("access_token=", setCookie, StringComparison.Ordinal);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AuthCookies_FollowAuthCookieSameSiteConfiguration()
    {
        await using var scenario = await _database.CreateScenarioAsync();
        var user = await scenario.CreateUserAsync("Cookie SameSite cross-site");

        var factory = new TeamNexusApiFactory
        {
            ConnectionString = TestScenario.ConnectionStringForTests,
            CookieSameSite = "None",
        };

        var setCookie = await RotateAndReadSetCookieAsync(factory, user);

        // The whole point of D7: a cross-site deployment (Vercel frontend + Render API) only receives
        // the session cookie when the flag follows configuration. A hard-coded Lax here is precisely
        // what makes a production login look successful yet stay signed out.
        Assert.Contains("access_token=", setCookie, StringComparison.Ordinal);
        Assert.Contains("samesite=none", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);

        // NOTE: `Secure` deliberately is NOT asserted here. TokenCookieService uses
        // `Secure = HttpContext.Request.IsHttps`, and the in-process test server speaks plain HTTP —
        // so on the real HTTPS deployment the flag is added, while here it must be absent. Asserting
        // it would be asserting a behaviour the transport does not have in tests.
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Drives a real cookie rotation on a private host and returns the raw <c>Set-Cookie</c> text.
    /// Only a successful refresh writes the auth cookies, so the session must be genuine: the
    /// antiforgery pair comes from that same host and the refresh token is issued by its AuthService.
    /// </summary>
    private static async Task<string> RotateAndReadSetCookieAsync(
        TeamNexusApiFactory factory, ApplicationUser user)
    {
        using var _ = factory;

        var jar = new System.Net.CookieContainer();
        var client = factory.CreateDefaultClient(new SessionCookieHandler(jar));
        client.BaseAddress = TestScenario.BaseAddress;

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var authService = scope.ServiceProvider.GetRequiredService<AuthService>();
            var pair = await authService.IssueTokenPairAsync(user);

            jar.Add(TestScenario.BaseAddress, new System.Net.Cookie(
                AuthConstants.RefreshTokenCookie, pair.RawRefreshToken, "/", "localhost"));
        }

        // Cookies must exist BEFORE the antiforgery round-trip: ASP.NET Core binds the token to the
        // claims-based user, so a token minted while anonymous is rejected once the request is signed.
        var antiforgery = await client.GetAsync("/api/auth/antiforgery");
        antiforgery.EnsureSuccessStatusCode();

        var token = ReadXsrfToken(antiforgery)!;
        client.DefaultRequestHeaders.Add(AuthConstants.XsrfRequestHeader, token);

        var refresh = await client.PostAsync("/api/auth/refresh", content: null);
        Assert.Equal(HttpStatusCode.NoContent, refresh.StatusCode);

        return refresh.Headers.TryGetValues("Set-Cookie", out var values)
            ? string.Join(";", values)
            : throw new InvalidOperationException("A successful refresh must write the auth cookies.");
    }

    private static string? ReadXsrfToken(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var headers))
        {
            return null;
        }

        foreach (var header in headers)
        {
            var first = header.Split(';')[0];
            var equals = first.IndexOf('=');

            if (equals > 0 && first[..equals].Trim() == AuthConstants.XsrfTokenCookie)
            {
                return Uri.UnescapeDataString(first[(equals + 1)..]);
            }
        }

        return null;
    }

    private sealed record HealthResponse(string Status, string Service, DateTimeOffset Time);

    private sealed record MeResponse(Guid Id, string? Email, string DisplayName, string? AvatarUrl, List<string> Roles);
}
