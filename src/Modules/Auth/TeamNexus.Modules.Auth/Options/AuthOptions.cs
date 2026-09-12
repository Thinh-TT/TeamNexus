using Microsoft.AspNetCore.Http;
using TeamNexus.Modules.Auth.Services;

namespace TeamNexus.Modules.Auth.Options;

/// <summary>
/// Cookie transport settings (section <c>Auth</c> in appsettings / env vars).
/// <para>
/// Deliberately a separate POCO from <see cref="JwtOptions"/>: the token's shape/validity and the way
/// it travels to the browser are different concerns, and this one applies to <b>both</b> the auth
/// cookies and the antiforgery cookie (see <c>AddAuthModule</c>).
/// </para>
/// <para>
/// Phase 8 D7: local development keeps <see cref="SameSiteMode.Lax"/> (frontend and API share the
/// <c>localhost</c> site through the Vite proxy), while a split deployment — frontend on Vercel, API
/// on Render — is cross-site and needs <see cref="SameSiteMode.None"/>. Forgetting this makes the
/// browser withhold the access cookie on every XHR, so a successful login still looks signed out.
/// </para>
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>
    /// <c>SameSite</c> for the access/refresh cookies and the antiforgery cookie.
    /// <para>
    /// Defaults to <see cref="SameSiteMode.Lax"/> so local behaviour is unchanged; a split deployment
    /// sets <c>Auth__CookieSameSite=None</c> (or <c>Unspecified</c>). <see cref="SameSiteMode.None"/>
    /// requires HTTPS — a browser discards a <c>None</c> cookie that arrives over plain HTTP.
    /// </para>
    /// </summary>
    public SameSiteMode CookieSameSite { get; set; } = SameSiteMode.Lax;
}
