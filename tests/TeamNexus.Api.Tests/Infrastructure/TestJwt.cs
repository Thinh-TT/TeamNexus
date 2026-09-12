using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Api.Tests.Infrastructure;

/// <summary>
/// Mints access tokens for the integration suites.
/// <para>
/// The claim set mirrors <c>JwtService.GenerateAccessToken</c> (name identifier + display name +
/// role claims) and the token is signed with the same key the host was configured with, so the
/// production <c>JwtBearer</c> pipeline — including <c>OnMessageReceived</c> reading the
/// HttpOnly cookie — is genuinely exercised. A different key is used by
/// <see cref="TeamNexusApiFactory.WrongSigningKey"/> to produce the 401 path.
/// </para>
/// </summary>
public static class TestJwt
{
    /// <summary>Creates a token for <paramref name="user"/>, optionally overriding issuer/audience/key/expiry.</summary>
    public static string Create(
        ApplicationUser user,
        string? role = null,
        string? signingKey = null,
        string issuer = "TeamNexus",
        string audience = "TeamNexus.Web",
        TimeSpan? lifetime = null)
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("jti", Guid.NewGuid().ToString("N")),
            new("display_name", user.DisplayName),
        };

        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, user.Email));
        }

        claims.Add(new Claim(ClaimTypes.Role, role ?? "Member"));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.AddMinutes(-1),
            IssuedAt = now,
            Expires = now.Add(lifetime ?? TimeSpan.FromMinutes(15)),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? TeamNexusApiFactory.TestSigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    /// <summary>Already-expired token (lifetime assertions).</summary>
    public static string CreateExpired(ApplicationUser user, string? role = null)
    {
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("jti", Guid.NewGuid().ToString("N")),
        };

        claims.Add(new Claim(ClaimTypes.Role, role ?? "Member"));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = "TeamNexus",
            Audience = "TeamNexus.Web",
            Subject = new ClaimsIdentity(claims),
            NotBefore = now.AddMinutes(-30),
            IssuedAt = now.AddMinutes(-30),
            Expires = now.AddMinutes(-10),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TeamNexusApiFactory.TestSigningKey)),
                SecurityAlgorithms.HmacSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
