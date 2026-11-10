namespace TeamNexus.Modules.Board.Services.Integrations;

using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Validates incoming GitHub webhook HMAC-SHA256 signatures (X-Hub-Signature-256)
/// using constant-time comparison to prevent timing attacks.
/// </summary>
public static class GitHubSignatureValidator
{
    public const string Sha256Prefix = "sha256=";

    /// <summary>
    /// Validates whether the given <paramref name="signatureHeader"/> matches the HMAC-SHA256
    /// computed from <paramref name="payloadBytes"/> and <paramref name="webhookSecret"/>.
    /// </summary>
    /// <param name="payloadBytes">The raw webhook payload bytes.</param>
    /// <param name="signatureHeader">The value of the X-Hub-Signature-256 header (e.g. "sha256=...").</param>
    /// <param name="webhookSecret">The webhook secret configured for the workspace.</param>
    /// <returns>True if signature matches; otherwise false.</returns>
    public static bool ValidateSignature(ReadOnlySpan<byte> payloadBytes, string? signatureHeader, string? webhookSecret)
    {
        if (payloadBytes.IsEmpty || string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(webhookSecret))
        {
            return false;
        }

        if (!signatureHeader.StartsWith(Sha256Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var providedHex = signatureHeader.AsSpan(Sha256Prefix.Length);
        if (providedHex.Length != 64)
        {
            return false;
        }

        byte[] providedHash;
        try
        {
            providedHash = Convert.FromHexString(providedHex);
        }
        catch (FormatException)
        {
            return false;
        }

        Span<byte> computedHash = stackalloc byte[32];
        byte[] keyBytes = Encoding.UTF8.GetBytes(webhookSecret);
        HMACSHA256.HashData(keyBytes, payloadBytes, computedHash);

        return CryptographicOperations.FixedTimeEquals(computedHash, providedHash);
    }

    /// <summary>
    /// Computes the GitHub signature header format ("sha256={lowercase_hex}")
    /// for a given payload and webhook secret.
    /// Useful for testing and dispatching webhook requests.
    /// </summary>
    /// <param name="payloadBytes">The raw payload bytes.</param>
    /// <param name="webhookSecret">The webhook secret key.</param>
    /// <returns>The signature header string, e.g. "sha256=...".</returns>
    public static string ComputeSignatureHeader(ReadOnlySpan<byte> payloadBytes, string webhookSecret)
    {
        ArgumentException.ThrowIfNullOrEmpty(webhookSecret);

        byte[] keyBytes = Encoding.UTF8.GetBytes(webhookSecret);
        Span<byte> hash = stackalloc byte[32];
        HMACSHA256.HashData(keyBytes, payloadBytes, hash);

        return $"{Sha256Prefix}{Convert.ToHexStringLower(hash)}";
    }
}
