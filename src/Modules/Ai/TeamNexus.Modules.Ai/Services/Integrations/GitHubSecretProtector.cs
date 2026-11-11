using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace TeamNexus.Modules.Ai.Services.Integrations;

/// <summary>
/// Implementation of <see cref="IGitHubSecretProtector"/> using ASP.NET Core Data Protection (Phase 15.3 §T15.3.1).
/// </summary>
public sealed class GitHubSecretProtector : IGitHubSecretProtector
{
    public const string Purpose = "TeamNexus.Integrations.GitHub.WebhookSecret.v1";

    private readonly IDataProtector _protector;

    public GitHubSecretProtector(IDataProtectionProvider dataProtectionProvider)
    {
        ArgumentNullException.ThrowIfNull(dataProtectionProvider);
        _protector = dataProtectionProvider.CreateProtector(Purpose);
    }

    public string GenerateNewSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        return _protector.Protect(plaintext);
    }

    public string Unprotect(string ciphertext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertext);
        return _protector.Unprotect(ciphertext);
    }

    public string GetHint(string plaintext)
    {
        if (string.IsNullOrWhiteSpace(plaintext))
        {
            return string.Empty;
        }

        var trimmed = plaintext.Trim();
        return trimmed.Length <= 4 ? trimmed : trimmed[^4..];
    }
}
