namespace TeamNexus.Modules.Auth.Options;

/// <summary>JWT configuration (section "Jwt" in appsettings / User Secrets).</summary>
public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "TeamNexus";

    public string Audience { get; set; } = "TeamNexus.Web";

    public int AccessTokenMinutes { get; set; } = 15;

    public int RefreshTokenDays { get; set; } = 7;

    /// <summary>HMAC-SHA256 signing key – must be at least 32 bytes (64 hex chars).</summary>
    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(AccessTokenMinutes);

    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(RefreshTokenDays);
}
