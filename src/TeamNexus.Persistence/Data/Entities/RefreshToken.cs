namespace TeamNexus.Persistence.Data.Entities;

/// <summary>
/// Refresh token record. Only the SHA-256 hash of the raw token is stored
/// (see Phase 1 §3.2). Table: refresh_tokens (DB design §3.2).
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid UserId { get; set; }

    /// <summary>SHA-256 hash of the raw refresh token. Never store the raw token.</summary>
    public string TokenHash { get; set; } = string.Empty;

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>Id of the token that replaced this one during rotation (replay detection).</summary>
    public Guid? ReplacedByTokenId { get; set; }

    public ApplicationUser? User { get; set; }

    public RefreshToken? ReplacedByToken { get; set; }
}
