namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Cryptographic protector for GitHub webhook secrets (Phase 15.3 §T15.3.1).
/// Generates high-entropy secrets and encrypts them at rest using ASP.NET Core Data Protection.
/// </summary>
public interface IGitHubSecretProtector
{
    /// <summary>Generates a 32-byte cryptographically secure random secret as a 64-character lowercase hex string.</summary>
    string GenerateNewSecret();

    /// <summary>Encrypts the plaintext secret for storage in the database.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts the stored ciphertext back into the plaintext secret for HMAC verification.</summary>
    string Unprotect(string ciphertext);

    /// <summary>Returns the last 4 characters of the plaintext secret for administrative identification.</summary>
    string GetHint(string plaintext);
}
