namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Validates GitHub webhook <c>X-Hub-Signature-256</c> request headers (Phase 15.3 §T15.3.1).
/// </summary>
public interface IGitHubSignatureValidator
{
    /// <summary>
    /// Validates the HMAC-SHA256 signature of the raw request body using constant-time comparison.
    /// </summary>
    /// <param name="rawBody">The raw bytes of the incoming request body exactly as received.</param>
    /// <param name="secretPlaintext">The plaintext webhook secret configured for the workspace.</param>
    /// <param name="signatureHeader">The value of the <c>X-Hub-Signature-256</c> HTTP header.</param>
    /// <returns><c>true</c> if the signature is valid; otherwise <c>false</c>.</returns>
    bool ValidateSignature(ReadOnlySpan<byte> rawBody, string secretPlaintext, string? signatureHeader);
}
