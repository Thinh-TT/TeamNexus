namespace TeamNexus.Modules.Board.Services.Integrations;

/// <summary>
/// Encrypts and decrypts sensitive workspace integration secrets (e.g. GitHub Personal Access Tokens,
/// Webhook Secrets) using ASP.NET Core Data Protection.
/// </summary>
public interface IIntegrationSecretProtector
{
    /// <summary>
    /// Encrypts plaintext secret. Returns empty string if input is null or empty.
    /// </summary>
    string Protect(string? plaintext);

    /// <summary>
    /// Decrypts protected ciphertext back into plaintext. Returns empty string if input is null or empty.
    /// Throws <see cref="System.Security.Cryptography.CryptographicException"/> if ciphertext was modified or invalid.
    /// </summary>
    string Unprotect(string? ciphertext);
}
