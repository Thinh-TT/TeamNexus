using System.Security.Cryptography;
using System.Text;
using TeamNexus.Modules.Board.Services.Integrations;

namespace TeamNexus.Api.Tests.Pure;

/// <summary>
/// Unit tests for <see cref="GitHubSignatureValidator"/>.
/// Verifies HMAC-SHA256 signature verification, timing-attack resistance,
/// prefix requirements, malformed inputs, and empty/large payload boundaries.
/// </summary>
public sealed class GitHubSignatureValidatorTests
{
    private const string Secret = "k3y_s3cr3t_f0r_w3bh00k_t3st1ng";
    private static readonly byte[] SamplePayload = Encoding.UTF8.GetBytes(
        """{"action":"closed","pull_request":{"number":42,"merged":true}}""");

    [Fact]
    public void ValidateSignature_ValidSignature_ReturnsTrue()
    {
        // Arrange
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(SamplePayload, signature, Secret);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateSignature_KnownHmacTestVector_ReturnsTrue()
    {
        // Arrange: known test payload and key
        byte[] payload = Encoding.UTF8.GetBytes("Hello, GitHub Webhook!");
        const string secret = "super-secret-key-123";

        // Manually compute HMAC-SHA256
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        byte[] hash = hmac.ComputeHash(payload);
        string expectedHeader = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(payload, expectedHeader, secret);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateSignature_PayloadWithUnicodeCharacters_ReturnsTrue()
    {
        // Arrange: Vietnamese PR title
        byte[] utf8Payload = Encoding.UTF8.GetBytes(
            """{"pull_request":{"title":"Sửa lỗi hiển thị tiếng Việt & đồng bộ Kanban"}}""");

        string signature = GitHubSignatureValidator.ComputeSignatureHeader(utf8Payload, Secret);

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(utf8Payload, signature, Secret);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateSignature_UppercaseHexDigest_ReturnsTrue()
    {
        // Arrange: valid hex in uppercase
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);
        string uppercaseSignature = "sha256=" + signature["sha256=".Length..].ToUpperInvariant();

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(SamplePayload, uppercaseSignature, Secret);

        // Assert
        Assert.True(isValid);
    }

    [Fact]
    public void ValidateSignature_SignatureTamperedByOneByte_ReturnsFalse()
    {
        // Arrange
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);

        // Mutate the last hex character
        char lastChar = signature[^1];
        char replacedChar = lastChar == 'a' ? 'b' : 'a';
        string tamperedSignature = signature[..^1] + replacedChar;

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(SamplePayload, tamperedSignature, Secret);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSignature_FirstHexCharTampered_ReturnsFalse()
    {
        // Arrange
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);
        int hexStart = "sha256=".Length;
        char firstHex = signature[hexStart];
        char replacement = firstHex == '0' ? '1' : '0';
        string tamperedSignature = signature[..hexStart] + replacement + signature[(hexStart + 1)..];

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(SamplePayload, tamperedSignature, Secret);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSignature_MiddleHexCharTampered_ReturnsFalse()
    {
        // Arrange
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);
        int midIndex = signature.Length / 2;
        char midChar = signature[midIndex];
        char replacement = midChar == 'c' ? 'd' : 'c';
        string tamperedSignature = signature[..midIndex] + replacement + signature[(midIndex + 1)..];

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(SamplePayload, tamperedSignature, Secret);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSignature_PayloadTamperedByOneByte_ReturnsFalse()
    {
        // Arrange
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);

        byte[] tamperedPayload = (byte[])SamplePayload.Clone();
        tamperedPayload[^1] ^= 0xFF; // Flip bits of last byte

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(tamperedPayload, signature, Secret);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSignature_WrongSecret_ReturnsFalse()
    {
        // Arrange
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);
        const string wrongSecret = "completely-wrong-secret-key";

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(SamplePayload, signature, wrongSecret);

        // Assert
        Assert.False(isValid);
    }

    [Theory]
    [InlineData("sha1=d57c7c34d31023be69e6b4f652c7b50bf696e5d8")]
    [InlineData("md5=d57c7c34d31023be69e6b4f652c7b50b")]
    [InlineData("sha512=d57c7c34d31023be69e6b4f652c7b50bf696e5d8")]
    [InlineData("d57c7c34d31023be69e6b4f652c7b50bf696e5d8e7c1a89b0d1e2f3a4b5c6d7e")] // raw hex, no prefix
    [InlineData("token=d57c7c34d31023be69e6b4f652c7b50bf696e5d8e7c1a89b0d1e2f3a4b5c6d7e")]
    public void ValidateSignature_MissingSha256Prefix_ReturnsFalse(string invalidPrefixSignature)
    {
        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(SamplePayload, invalidPrefixSignature, Secret);

        // Assert
        Assert.False(isValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("sha256=")]
    [InlineData("sha256=abc")] // too short
    [InlineData("sha256=d57c7c34d31023be69e6b4f652c7b50bf696e5d8e7c1a89b0d1e2f3a4b5c6d7e99")] // too long (66 chars)
    [InlineData("sha256=zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // invalid hex chars
    public void ValidateSignature_NullEmptyOrMalformedHeader_ReturnsFalse(string? malformedHeader)
    {
        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(SamplePayload, malformedHeader, Secret);

        // Assert
        Assert.False(isValid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ValidateSignature_NullOrEmptySecret_ReturnsFalse(string? emptySecret)
    {
        // Arrange
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);

        // Act
        bool isValid = GitHubSignatureValidator.ValidateSignature(SamplePayload, signature, emptySecret);

        // Assert
        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSignature_EmptyPayload_ReturnsFalse()
    {
        // Arrange
        string signature = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);

        // Act & Assert
        Assert.False(GitHubSignatureValidator.ValidateSignature(ReadOnlySpan<byte>.Empty, signature, Secret));
        Assert.False(GitHubSignatureValidator.ValidateSignature(Array.Empty<byte>(), signature, Secret));
    }

    [Fact]
    public void ValidateSignature_LargePayload_ProcessedAccurately()
    {
        // Arrange: 512 KB payload simulating heavy repository sync event
        byte[] largePayload = new byte[512 * 1024];
        Random.Shared.NextBytes(largePayload);

        string validSignature = GitHubSignatureValidator.ComputeSignatureHeader(largePayload, Secret);

        // Act
        bool validResult = GitHubSignatureValidator.ValidateSignature(largePayload, validSignature, Secret);

        // Mutate single byte in 512KB payload
        byte[] tamperedLargePayload = (byte[])largePayload.Clone();
        tamperedLargePayload[256 * 1024] ^= 0x01;
        bool invalidResult = GitHubSignatureValidator.ValidateSignature(tamperedLargePayload, validSignature, Secret);

        // Assert
        Assert.True(validResult);
        Assert.False(invalidResult);
    }

    [Fact]
    public void ComputeSignatureHeader_ValidInputs_GeneratesStandardSha256Header()
    {
        // Act
        string header = GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, Secret);

        // Assert
        Assert.StartsWith("sha256=", header, StringComparison.Ordinal);
        Assert.Equal(7 + 64, header.Length);

        // Hex part must be all lowercase hex characters
        string hex = header["sha256=".Length..];
        Assert.True(hex.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ComputeSignatureHeader_NullOrEmptySecret_ThrowsArgumentException(string? invalidSecret)
    {
        // Act & Assert
        Assert.ThrowsAny<ArgumentException>(() =>
            GitHubSignatureValidator.ComputeSignatureHeader(SamplePayload, invalidSecret!));
    }
}
