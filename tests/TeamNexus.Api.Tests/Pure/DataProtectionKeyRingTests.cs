using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Phase 15.3 review F3: the webhook secret must survive a process restart. Encrypting with
/// <c>IDataProtectionProvider</c> alone is not enough — the default key ring lives in the machine
/// profile, and a container redeploy throws it away, turning every stored <c>SecretCiphertext</c> into
/// garbage (GitHub then gets 401 for every delivery).
/// <para>
/// These tests pin the mechanism the API now configures from
/// <c>Integrations:DataProtection:KeysPath</c>: two independent providers sharing one key directory must
/// be able to decrypt each other's payloads, which is exactly what "survives a restart" means.
/// </para>
/// </summary>
public sealed class DataProtectionKeyRingTests : IDisposable
{
    private const string Purpose = "TeamNexus.Integrations.GitHub.WebhookSecret.v1";

    private readonly string _keysDirectory =
        Path.Combine(Path.GetTempPath(), "teamnexus-dp-tests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_keysDirectory))
            {
                Directory.Delete(_keysDirectory, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup: a locked key file must never fail the suite.
        }
    }

    private IDataProtectionProvider NewProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection()
            .SetApplicationName("TeamNexus")
            .PersistKeysToFileSystem(new DirectoryInfo(_keysDirectory));

        return services.BuildServiceProvider().GetRequiredService<IDataProtectionProvider>();
    }

    [Fact]
    public void PersistedKeyRing_LetsASecondProviderUnprotectTheFirstProvidersSecret()
    {
        var secret = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var first = NewProvider().CreateProtector(Purpose);
        var ciphertext = first.Protect(secret);

        // A brand-new provider (a restarted process) reading the same directory must decrypt it.
        var second = NewProvider().CreateProtector(Purpose);

        Assert.Equal(secret, second.Unprotect(ciphertext));
    }

    [Fact]
    public void PersistedKeyRing_WritesKeyMaterialToTheConfiguredDirectory()
    {
        _ = NewProvider().CreateProtector(Purpose).Protect("warm-up");

        Assert.True(Directory.Exists(_keysDirectory));
        var keyFiles = Directory.GetFiles(_keysDirectory, "key-*.xml");
        Assert.NotEmpty(keyFiles);
    }

    [Fact]
    public void DifferentPurpose_CannotUnprotectTheWebhookSecret()
    {
        var provider = NewProvider();
        var ciphertext = provider.CreateProtector(Purpose).Protect("secret");

        var otherPurpose = provider.CreateProtector("TeamNexus.Integrations.v1.SomethingElse");

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(
            () => otherPurpose.Unprotect(ciphertext));
    }
}
