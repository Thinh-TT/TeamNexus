using System.Globalization;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TeamNexus.Modules.Auth.Options;
using TeamNexus.Persistence.Data;
using TeamNexus.Persistence.Data.Entities;

namespace TeamNexus.Modules.Auth.Services;

/// <summary>
/// Auth business logic (no HttpContext dependency): external-login user upsert,
/// token pair issuance and refresh-token rotation/revocation (Phase 1 §3.1–§3.2).
/// </summary>
public sealed class AuthService
{
    public sealed record TokenPair(string AccessToken, string RawRefreshToken);

    // Claim types carrying the display name / avatar, across providers
    // (GitHub: urn:github:*, Google: name + urn:google:picture, generic: picture/Uri).
    private static readonly string[] DisplayNameClaimTypes =
        ["name", "display_name", ClaimTypes.Name, "login", "urn:github:login"];

    private static readonly string[] AvatarClaimTypes =
        ["avatar", "avatar_url", "urn:github:avatar", "urn:google:picture", "picture", ClaimTypes.Uri];

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TeamNexusDbContext _db;
    private readonly JwtService _jwt;
    private readonly JwtOptions _jwtOptions;
    private readonly ILogger<AuthService> _logger;

    public AuthService(
        UserManager<ApplicationUser> userManager,
        TeamNexusDbContext db,
        JwtService jwt,
        IOptions<JwtOptions> jwtOptions,
        ILogger<AuthService> logger)
    {
        _userManager = userManager;
        _db = db;
        _jwt = jwt;
        _jwtOptions = jwtOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Upserts a user from external (OAuth) claims and links the provider login.
    /// Matching order: provider login → email → create new account.
    /// Returns the user and whether it was just created.
    /// </summary>
    public async Task<(ApplicationUser User, bool IsNewUser)> HandleExternalLoginAsync(
        string provider,
        IEnumerable<Claim> claims,
        CancellationToken ct = default)
    {
        var claimList = claims.ToList();
        _logger.LogDebug("External login '{Provider}' claims: {Claims}",
            provider, string.Join(", ", claimList.Select(c => $"{c.Type}={c.Value}")));

        var providerKey = FindFirst(claimList, ClaimTypes.NameIdentifier, "sub")
            ?? throw new InvalidOperationException($"OAuth principal from '{provider}' has no user id.");

        var user = await _userManager.FindByLoginAsync(provider, providerKey);

        if (user is not null)
        {
            await ApplyProfileAsync(user, claimList, ct);
            return (user, false);
        }

        var email = FindFirst(claimList, ClaimTypes.Email, "email");
        if (!string.IsNullOrWhiteSpace(email))
        {
            user = await _userManager.FindByEmailAsync(email);
        }

        if (user is not null)
        {
            // Same person logged in with another provider before → link this login.
            await _userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, provider));
            await ApplyProfileAsync(user, claimList, ct);
            return (user, false);
        }

        // New account.
        var userName = await CreateUniqueUserNameAsync(providerKey, claimList, ct);
        var displayName = FindFirst(claimList, DisplayNameClaimTypes)
                          ?? userName;

        user = new ApplicationUser
        {
            UserName = userName,
            Email = string.IsNullOrWhiteSpace(email) ? null : email,
            EmailConfirmed = !string.IsNullOrWhiteSpace(email),
            DisplayName = displayName,
            AvatarUrl = FindFirst(claimList, AvatarClaimTypes),
        };

        var createResult = await _userManager.CreateAsync(user);
        if (!createResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to create user: {string.Join("; ", createResult.Errors.Select(e => e.Description))}");
        }

        await _userManager.AddLoginAsync(user, new UserLoginInfo(provider, providerKey, provider));

        // Default global role for new accounts (RBAC, Phase 1 §3.3).
        var roleResult = await _userManager.AddToRoleAsync(user, AuthConstants.DefaultMemberRole);
        if (!roleResult.Succeeded)
        {
            _logger.LogWarning("Could not assign default role to new user: {Errors}",
                string.Join("; ", roleResult.Errors.Select(e => e.Description)));
        }

        return (user, true);
    }

    /// <summary>Issues an access token + persists a fresh refresh token.</summary>
    public async Task<TokenPair> IssueTokenPairAsync(ApplicationUser user, CancellationToken ct = default)
    {
        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _jwt.GenerateAccessToken(user, roles);
        var rawRefresh = JwtService.GenerateRefreshToken();

        _db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            TokenHash = JwtService.HashToken(rawRefresh),
            ExpiresAt = DateTimeOffset.UtcNow.Add(_jwtOptions.RefreshTokenLifetime),
        });

        await _db.SaveChangesAsync(ct);

        return new TokenPair(accessToken, rawRefresh);
    }

    /// <summary>
    /// Rotates a refresh token: revokes the presented token and issues a new pair.
    /// Returns null (→ 401) when the token is unknown, expired, or already revoked.
    /// Reuse of a revoked token revokes the whole family of that user (replay defense).
    /// </summary>
    public async Task<TokenPair?> RotateRefreshTokenAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var hash = JwtService.HashToken(rawRefreshToken);
        var token = await _db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null)
        {
            return null;
        }

        // Replay: a revoked/rotated token was presented again.
        if (token.RevokedAt is not null)
        {
            _logger.LogWarning("Refresh token reuse detected (user {UserId}); revoking family.", token.UserId);
            await RevokeAllActiveForUserAsync(token.UserId, ct);
            return null;
        }

        if (token.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return null;
        }

        var user = await _userManager.FindByIdAsync(token.UserId.ToString());
        if (user is null)
        {
            return null;
        }

        var roles = await _userManager.GetRolesAsync(user);
        var accessToken = _jwt.GenerateAccessToken(user, roles);
        var rawNewRefresh = JwtService.GenerateRefreshToken();

        var replacement = new RefreshToken
        {
            UserId = token.UserId,
            TokenHash = JwtService.HashToken(rawNewRefresh),
            ExpiresAt = DateTimeOffset.UtcNow.Add(_jwtOptions.RefreshTokenLifetime),
            // New token points back at the one it replaced (rotation chain, DB design §3.2).
            ReplacedByTokenId = token.Id,
        };

        token.RevokedAt = DateTimeOffset.UtcNow;

        _db.RefreshTokens.Add(replacement);
        await _db.SaveChangesAsync(ct);

        return new TokenPair(accessToken, rawNewRefresh);
    }

    /// <summary>Revokes the presented refresh token (logout).</summary>
    public async Task RevokeRefreshTokenAsync(string rawRefreshToken, CancellationToken ct = default)
    {
        var hash = JwtService.HashToken(rawRefreshToken);
        var token = await _db.RefreshTokens
            .SingleOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is not null && token.RevokedAt is null)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
    }

    private async Task ApplyProfileAsync(ApplicationUser user, List<Claim> claims, CancellationToken ct)
    {
        var changed = false;

        var displayName = FindFirst(claims, DisplayNameClaimTypes);
        if (displayName is not null && user.DisplayName != displayName)
        {
            user.DisplayName = displayName;
            changed = true;
        }

        var avatar = FindFirst(claims, AvatarClaimTypes);
        if (avatar is not null && user.AvatarUrl != avatar)
        {
            user.AvatarUrl = avatar;
            changed = true;
        }

        if (changed)
        {
            var result = await _userManager.UpdateAsync(user);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Profile update failed: {Errors}",
                    string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }
    }

    private async Task<string> CreateUniqueUserNameAsync(string providerKey, List<Claim> claims, CancellationToken ct)
    {
        var email = FindFirst(claims, ClaimTypes.Email, "email");
        var emailPrefix = !string.IsNullOrWhiteSpace(email) ? email.Split('@')[0] : null;
        var rawName = FindFirst(claims, "login", "urn:github:login") ?? emailPrefix ?? FindFirst(claims, ClaimTypes.Name) ?? providerKey;

        var baseName = SanitizeUserName(rawName);
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = $"user_{providerKey}";
        }

        var candidate = baseName;
        var suffix = 2;

        while (await _userManager.FindByNameAsync(candidate) is not null)
        {
            var truncated = baseName.Length > 230 ? baseName[..230] : baseName;
            candidate = $"{truncated}_{suffix++}";
        }

        return candidate;
    }

    private static string SanitizeUserName(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var normalized = input.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in normalized)
        {
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-' || c == '.')
                {
                    sb.Append(c);
                }
            }
        }

        return sb.ToString();
    }

    private async Task RevokeAllActiveForUserAsync(Guid userId, CancellationToken ct)
    {
        var active = await _db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ToListAsync(ct);

        foreach (var token in active)
        {
            token.RevokedAt = DateTimeOffset.UtcNow;
        }

        if (active.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
        }
    }

    private static string? FindFirst(IEnumerable<Claim> claims, params string[] types)
    {
        foreach (var type in types)
        {
            var value = claims.FirstOrDefault(c => c.Type == type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }
}
