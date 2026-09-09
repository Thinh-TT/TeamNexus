namespace TeamNexus.Modules.Auth;

/// <summary>Shared auth constants (schemes, cookies, policies, default role).</summary>
public static class AuthConstants
{
    // OAuth provider schemes.
    public const string GitHubScheme = "GitHub";
    public const string GoogleScheme = "Google"; // enabled in a later step of Phase 1 §3

    // Temporary cookie scheme used between the OAuth callback and our own
    // /api/auth/external-login endpoint (classic external-login flow).
    public const string ExternalScheme = "TeamNexus.External";

    // Cookies set on the API responses.
    public const string AccessTokenCookie = "access_token";
    public const string RefreshTokenCookie = "refresh_token";

    // Anti-CSRF (Phase 1 §3.2): cookie carries the request token, header echoes it.
    public const string XsrfTokenCookie = "XSRF-TOKEN";
    public const string XsrfRequestHeader = "X-XSRF-TOKEN";

    // Authorization policies (Phase 1 §3.3).
    public const string AdminOnlyPolicy = "AdminOnly";
    public const string ManagerOrAbovePolicy = "ManagerOrAbove";
    public const string MemberOrAbovePolicy = "MemberOrAbove";

    // Default global role assigned to new OAuth accounts.
    public const string DefaultMemberRole = "Member";

    public const string GitHubCallbackPath = "/api/auth/callback/github";
    public const string ExternalLoginPath = "/api/auth/external-login";
}
