namespace TeamNexus.Modules.Board.Services.Integrations;

using Microsoft.AspNetCore.DataProtection;

/// <summary>
/// Default implementation of <see cref="IIntegrationSecretProtector"/> backed by ASP.NET Core Data Protection.
/// Purpose string is isolated to "TeamNexus.WorkspaceIntegrations.v1".
/// </summary>
public sealed class IntegrationSecretProtector : IIntegrationSecretProtector
{
    public const string Purpose = "TeamNexus.WorkspaceIntegrations.v1";
    private readonly IDataProtector _protector;

    public IntegrationSecretProtector(IDataProtectionProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _protector = provider.CreateProtector(Purpose);
    }

    /// <inheritdoc />
    public string Protect(string? plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return string.Empty;
        }

        return _protector.Protect(plaintext);
    }

    /// <inheritdoc />
    public string Unprotect(string? ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            return string.Empty;
        }

        return _protector.Unprotect(ciphertext);
    }
}
