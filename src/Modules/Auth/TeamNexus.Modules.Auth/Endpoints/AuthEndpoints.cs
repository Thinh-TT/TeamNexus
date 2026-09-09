using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TeamNexus.Modules.Auth.Services;
using TeamNexus.Persistence.Data.Entities;
using TeamNexus.Shared.Endpoints;

namespace TeamNexus.Modules.Auth.Endpoints;

/// <summary>
/// Auth HTTP surface (Phase 1 §3): OAuth login, JWT cookie endpoints, anti-CSRF
/// and RBAC sample endpoints. Registered from Program.cs via MapAuthModuleEndpoints().
/// </summary>
public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthModuleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth").WithTags("Auth");

        // GET /api/auth/login/github → GitHub consent screen.
        auth.MapGet("/login/{provider}", LoginAsync).AllowAnonymous();

        // Internal step after the OAuth provider redirect: consumes the temporary
        // external cookie, upserts the user and issues JWT cookies. Redirects to the
        // frontend. (The provider callback itself is handled by the OAuth handler at
        // /api/auth/callback/github, which is why no endpoint is mapped there.)
        auth.MapGet("/external-login", ExternalLoginAsync).AllowAnonymous();

        auth.MapGet("/me", MeAsync).RequireAuthorization(AuthConstants.MemberOrAbovePolicy);

        // Mutating cookie endpoints are CSRF-protected (X-XSRF-TOKEN header).
        auth.MapPost("/refresh", RefreshAsync)
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>()
            .AllowAnonymous();

        auth.MapPost("/logout", LogoutAsync)
            .AddEndpointFilter<AntiforgeryValidationEndpointFilter>()
            .AllowAnonymous();

        // Issues the anti-CSRF request token (also mirrored into XSRF-TOKEN cookie).
        auth.MapGet("/antiforgery", AntiforgeryAsync).AllowAnonymous();

        // ---- RBAC sample endpoints (Phase 1 §3.3) ----
        endpoints.MapGet("/api/admin/ping", () => Results.Ok(new { message = "Admin access OK." }))
            .RequireAuthorization(AuthConstants.AdminOnlyPolicy)
            .WithTags("Sample");

        endpoints.MapGet("/api/manager/ping", () => Results.Ok(new { message = "Manager access OK." }))
            .RequireAuthorization(AuthConstants.ManagerOrAbovePolicy)
            .WithTags("Sample");

        return endpoints;
    }

    private static async Task<IResult> LoginAsync(
        string provider,
        HttpContext http,
        IAuthenticationSchemeProvider schemeProvider)
    {
        var scheme = provider.ToLowerInvariant() switch
        {
            "github" => AuthConstants.GitHubScheme,
            "google" => AuthConstants.GoogleScheme,
            _ => null,
        };

        if (scheme is null)
        {
            return Results.NotFound(new { error = $"Unknown provider '{provider}'." });
        }

        if (await schemeProvider.GetSchemeAsync(scheme) is null)
        {
            return Results.BadRequest(new { error = $"Provider '{provider}' is not configured yet." });
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = $"{http.Request.Scheme}://{http.Request.Host}{AuthConstants.ExternalLoginPath}",
        };
        properties.Items["login_provider"] = scheme;

        return Results.Challenge(properties, [scheme]);
    }

    private static async Task<IResult> ExternalLoginAsync(
        HttpContext http,
        AuthService authService,
        TokenCookieService cookies,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("TeamNexus.Modules.Auth.ExternalLogin");
        var frontendBaseUrl = configuration["Frontend:BaseUrl"] ?? "http://localhost:5173";

        var result = await http.AuthenticateAsync(AuthConstants.ExternalScheme);

        if (!result.Succeeded || result.Principal is null)
        {
            await http.SignOutAsync(AuthConstants.ExternalScheme);
            return Results.Redirect($"{frontendBaseUrl}?auth=error");
        }

        try
        {
            var provider = result.Properties?.Items.TryGetValue("login_provider", out var value) == true
                           && !string.IsNullOrEmpty(value)
                ? value!
                : AuthConstants.GitHubScheme;

            var (user, _) = await authService.HandleExternalLoginAsync(
                provider, result.Principal.Claims, http.RequestAborted);

            var pair = await authService.IssueTokenPairAsync(user, http.RequestAborted);
            cookies.SetAuthCookies(pair.AccessToken, pair.RawRefreshToken);

            await http.SignOutAsync(AuthConstants.ExternalScheme);

            return Results.Redirect(frontendBaseUrl);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "External login failed for {Provider}.", AuthConstants.GitHubScheme);
            await http.SignOutAsync(AuthConstants.ExternalScheme);
            return Results.Redirect($"{frontendBaseUrl}?auth=error");
        }
    }

    private static async Task<IResult> MeAsync(
        HttpContext http,
        UserManager<ApplicationUser> userManager)
    {
        var user = await userManager.GetUserAsync(http.User);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var roles = await userManager.GetRolesAsync(user);

        return Results.Ok(new
        {
            id = user.Id,
            email = user.Email,
            displayName = user.DisplayName,
            avatarUrl = user.AvatarUrl,
            roles,
        });
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext http,
        AuthService authService,
        TokenCookieService cookies)
    {
        var rawRefresh = cookies.RefreshToken;
        if (string.IsNullOrEmpty(rawRefresh))
        {
            return Results.Unauthorized();
        }

        var pair = await authService.RotateRefreshTokenAsync(rawRefresh, http.RequestAborted);

        if (pair is null)
        {
            cookies.ClearAuthCookies();
            return Results.Unauthorized();
        }

        cookies.SetAuthCookies(pair.AccessToken, pair.RawRefreshToken);
        return Results.NoContent();
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext http,
        AuthService authService,
        TokenCookieService cookies)
    {
        var rawRefresh = cookies.RefreshToken;
        if (!string.IsNullOrEmpty(rawRefresh))
        {
            await authService.RevokeRefreshTokenAsync(rawRefresh, http.RequestAborted);
        }

        cookies.ClearAuthCookies();
        return Results.NoContent();
    }

    private static IResult AntiforgeryAsync(HttpContext http, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(http);

        if (tokens.RequestToken is not null)
        {
            http.Response.Cookies.Append(AuthConstants.XsrfTokenCookie, tokens.RequestToken, new CookieOptions
            {
                HttpOnly = false, // readable by the SPA so it can echo it as a header
                Secure = http.Request.IsHttps,
                SameSite = SameSiteMode.Lax,
                Path = "/",
            });
        }

        return Results.NoContent();
    }
}
