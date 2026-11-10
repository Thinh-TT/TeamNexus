using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using TeamNexus.Modules.Board.Services.Integrations;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Unit tests for <see cref="IntegrationSecretProtector"/>.
/// Verifies round-trip encryption/decryption of workspace secrets (PAT, Webhook secrets),
/// tamper detection via CryptographicException, and safe handling of null/empty inputs.
/// </summary>
public sealed class IntegrationSecretProtectorTests
{
    private readonly EphemeralDataProtectionProvider _provider = new();
    private readonly IntegrationSecretProtector _protector;

    public IntegrationSecretProtectorTests()
    {
        _protector = new IntegrationSecretProtector(_provider);
    }

    [Theory]
    [InlineData("ghp_1234567890abcdefghijklmnopqrstuvwxyzAB")]
    [InlineData("github_pat_11ABCD_efgh9876543210zyxwvutsrqponmlkjihgfedcba")]
    [InlineData("whsec_super_secret_webhook_key_2026_production")]
    [InlineData("Khóa-Bảo-Mật-Tiếng-Việt-100%-An-Toàn!@#$%^&*()_+")]
    [InlineData("Special chars: ~`!@#$%^&*()_+-=[]{}|;':\",./<>?")]
    public void ProtectAndUnprotect_RoundtripsOriginalPlaintext(string plaintext)
    {
        // Act
        string ciphertext = _protector.Protect(plaintext);
        string decrypted = _protector.Unprotect(ciphertext);

        // Assert
        Assert.False(string.IsNullOrWhiteSpace(ciphertext));
        Assert.NotEqual(plaintext, ciphertext);
        Assert.Equal(plaintext, decrypted);
    }

    [Fact]
    public void ProtectAndUnprotect_LargePlaintextSecret_RoundtripsSuccessfully()
    {
        // Arrange: 8KB token or certificate payload
        string largePlaintext = new('x', 8192);

        // Act
        string ciphertext = _protector.Protect(largePlaintext);
        string decrypted = _protector.Unprotect(ciphertext);

        // Assert
        Assert.Equal(largePlaintext, decrypted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Protect_NullOrEmptyInput_ReturnsEmptyString(string? input)
    {
        // Act
        string result = _protector.Protect(input);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Unprotect_NullOrEmptyInput_ReturnsEmptyString(string? input)
    {
        // Act
        string result = _protector.Unprotect(input);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_ThrowsCryptographicException()
    {
        // Arrange
        const string plaintext = "ghp_sensitive_secret_token_12345";
        string ciphertext = _protector.Protect(plaintext);

        // Tamper with middle character
        char[] chars = ciphertext.ToCharArray();
        int midIndex = chars.Length / 2;
        chars[midIndex] = chars[midIndex] == 'A' ? 'B' : 'A';
        string tamperedCiphertext = new(chars);

        // Act & Assert
        Assert.Throws<CryptographicException>(() => _protector.Unprotect(tamperedCiphertext));
    }

    [Fact]
    public void Unprotect_TruncatedCiphertext_ThrowsCryptographicException()
    {
        // Arrange
        string ciphertext = _protector.Protect("ghp_my_test_pat");
        string truncated = ciphertext[..(ciphertext.Length / 2)];

        // Act & Assert
        Assert.Throws<CryptographicException>(() => _protector.Unprotect(truncated));
    }

    [Theory]
    [InlineData("not-a-valid-ciphertext")]
    [InlineData("AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=")]
    [InlineData("???malformed_base64???")]
    public void Unprotect_MalformedGarbage_ThrowsCryptographicException(string garbage)
    {
        // Act & Assert
        Assert.Throws<CryptographicException>(() => _protector.Unprotect(garbage));
    }

    [Fact]
    public void Constructor_NullProvider_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new IntegrationSecretProtector(null!));
    }

    [Fact]
    public void CrossPurposeIsolation_CiphertextFromDifferentPurpose_ThrowsCryptographicException()
    {
        // Arrange: a protector created with a different purpose
        var otherProtector = _provider.CreateProtector("SomeOther.Module.Purpose.v1");
        string otherCiphertext = otherProtector.Protect("my-secret");

        // Act & Assert: IntegrationSecretProtector should refuse to unprotect it
        Assert.Throws<CryptographicException>(() => _protector.Unprotect(otherCiphertext));
    }

    [Fact]
    public void Protect_SamePlaintextTwice_ProducesNondeterministicCiphertextsBothDecrypted()
    {
        // Arrange: ASP.NET Core Data Protection uses randomized IV / timestamp
        const string secret = "ghp_nondeterministic_test";

        // Act
        string cipher1 = _protector.Protect(secret);
        string cipher2 = _protector.Protect(secret);

        // Assert: IV randomization means ciphertexts differ
        Assert.NotEqual(cipher1, cipher2);
        Assert.Equal(secret, _protector.Unprotect(cipher1));
        Assert.Equal(secret, _protector.Unprotect(cipher2));
    }
}
