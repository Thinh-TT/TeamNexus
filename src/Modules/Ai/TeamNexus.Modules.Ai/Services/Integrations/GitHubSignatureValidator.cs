using System.Security.Cryptography;
using System.Text;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Constant-time HMAC-SHA256 signature validator for GitHub webhooks (Phase 15.3 §T15.3.1).
/// </summary>
public sealed class GitHubSignatureValidator : IGitHubSignatureValidator
{
    private const string SignaturePrefix = "sha256=";
    private const int ExpectedHashLength = 32;
    private const int ExpectedHexLength = ExpectedHashLength * 2; // 64 chars

    public bool ValidateSignature(ReadOnlySpan<byte> rawBody, string secretPlaintext, string? signatureHeader)
    {
        if (string.IsNullOrWhiteSpace(signatureHeader) || string.IsNullOrEmpty(secretPlaintext))
        {
            return false;
        }

        var headerSpan = signatureHeader.AsSpan().Trim();
        if (!headerSpan.StartsWith(SignaturePrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var hexSpan = headerSpan[SignaturePrefix.Length..];
        if (hexSpan.Length != ExpectedHexLength)
        {
            return false;
        }

        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(hexSpan);
        }
        catch (FormatException)
        {
            return false;
        }

        if (expectedHash.Length != ExpectedHashLength)
        {
            return false;
        }

        var secretBytes = Encoding.UTF8.GetBytes(secretPlaintext);
        Span<byte> computedHash = stackalloc byte[ExpectedHashLength];
        HMACSHA256.HashData(secretBytes, rawBody, computedHash);

        return CryptographicOperations.FixedTimeEquals(expectedHash, computedHash);
    }
}
