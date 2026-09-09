using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Auth.Options;

namespace TeamNexus.Modules.Auth.Services;

/// <summary>
/// Writes/clears the HttpOnly auth cookies on responses and reads them from requests.
/// `Secure` is applied only over HTTPS so dev (plain http://localhost) still works;
/// behind TLS (prod) the flag is always on.
/// </summary>
public sealed class TokenCookieService
{
    private readonly IHttpContextAccessor _accessor;
    private readonly JwtOptions _jwtOptions;

    public TokenCookieService(IHttpContextAccessor accessor, IOptions<JwtOptions> jwtOptions)
    {
        _accessor = accessor;
        _jwtOptions = jwtOptions.Value;
    }

    private HttpContext HttpContext =>
        _accessor.HttpContext
        ?? throw new InvalidOperationException("No active HttpContext (TokenCookieService must run inside a request).");

    public string? AccessToken => HttpContext.Request.Cookies[AuthConstants.AccessTokenCookie];

    public string? RefreshToken => HttpContext.Request.Cookies[AuthConstants.RefreshTokenCookie];

    public void SetAuthCookies(string accessToken, string rawRefreshToken)
    {
        var now = DateTimeOffset.UtcNow;

        HttpContext.Response.Cookies.Append(AuthConstants.AccessTokenCookie, accessToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = now.Add(_jwtOptions.AccessTokenLifetime),
        });

        HttpContext.Response.Cookies.Append(AuthConstants.RefreshTokenCookie, rawRefreshToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = now.Add(_jwtOptions.RefreshTokenLifetime),
        });
    }

    public void ClearAuthCookies()
    {
        var expired = DateTimeOffset.UtcNow.AddDays(-1);

        HttpContext.Response.Cookies.Append(AuthConstants.AccessTokenCookie, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expired,
        });

        HttpContext.Response.Cookies.Append(AuthConstants.RefreshTokenCookie, string.Empty, new CookieOptions
        {
            HttpOnly = true,
            Secure = HttpContext.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/",
            Expires = expired,
        });
    }
}
