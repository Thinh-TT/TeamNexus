namespace TeamNexus.Modules.Auth;

/// <summary>Shared auth constants (schemes, cookies, policies, default role).</summary>
public static class AuthConstants
{
    // OAuth provider schemes.
    public const string GitHubScheme = "GitHub";
    public const string GoogleScheme = "Google";

    // Temporary cookie scheme used between the OAuth callback and our own
    // /api/auth/external-login endpoint (classic external-login flow).
    public const string ExternalScheme = "TeamNexus.External";

    // Cookies set on the API responses.
    public const string AccessTokenCookie = "access_token";
    public const string RefreshTokenCookie = "refresh_token";

    // Anti-CSRF (Phase 1 §3.2): cookie carries the request token, header echoes it.
    public const string XsrfTokenCookie = "XSRF-TOKEN";
    public const string XsrfRequestHeader = "X-XSRF-TOKEN";

    // Authorization policies (Phase 9 — simplified role architecture).
    // SystemAdminPolicy: Identity role = "Admin" (platform-level admin only).
    // All workspace-level endpoints use plain .RequireAuthorization() (any authenticated user)
    // and check workspace_members.role inside the service layer.
    public const string SystemAdminPolicy = "SystemAdmin";

    // Default global Identity role assigned to new OAuth accounts.
    public const string DefaultUserRole = "User";

    public const string GitHubCallbackPath = "/api/auth/callback/github";
    public const string GoogleCallbackPath = "/api/auth/callback/google";
    public const string ExternalLoginPath = "/api/auth/external-login";
}

