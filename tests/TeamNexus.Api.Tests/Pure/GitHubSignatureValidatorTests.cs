using System.Security.Cryptography;
using System.Text;
using TeamNexus.Modules.Ai.Services.Integrations;

namespace TeamNexus.Api.Tests.Pure;

public sealed class GitHubSignatureValidatorTests
{
    private readonly GitHubSignatureValidator _validator = new();

    private static string ComputeHeader(string secret, byte[] body)
    {
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(secretBytes, body);
        return $"sha256={Convert.ToHexString(hash).ToLowerInvariant()}";
    }

    [Fact]
    public void ValidateSignature_ValidSignature_ReturnsTrue()
    {
        var secret = "super_secret_webhook_key_12345678";
        var body = Encoding.UTF8.GetBytes("{\"action\":\"opened\",\"number\":42}");
        var header = ComputeHeader(secret, body);

        var isValid = _validator.ValidateSignature(body, secret, header);

        Assert.True(isValid);
    }

    [Fact]
    public void ValidateSignature_TamperedBody_ReturnsFalse()
    {
        var secret = "super_secret_webhook_key_12345678";
        var originalBody = Encoding.UTF8.GetBytes("{\"action\":\"opened\",\"number\":42}");
        var header = ComputeHeader(secret, originalBody);

        var tamperedBody = Encoding.UTF8.GetBytes("{\"action\":\"opened\",\"number\":43}");
        var isValid = _validator.ValidateSignature(tamperedBody, secret, header);

        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSignature_UppercaseSha256Header_ReturnsTrue()
    {
        var secret = "super_secret_webhook_key_12345678";
        var body = Encoding.UTF8.GetBytes("{\"zen\":\"Keep it logically awesome.\"}");
        var secretBytes = Encoding.UTF8.GetBytes(secret);
        var hash = HMACSHA256.HashData(secretBytes, body);
        var header = $"SHA256={Convert.ToHexString(hash).ToUpperInvariant()}";

        var isValid = _validator.ValidateSignature(body, secret, header);

        Assert.True(isValid);
    }

    [Theory]
    [InlineData("sha1=757107ea0eb2509fc211221cce984b8a37570b6d")] // Unsupported algorithm
    [InlineData("sha256=invalid_hex_string")] // Not hex
    [InlineData("sha256=12345")] // Wrong length
    [InlineData("757107ea0eb2509fc211221cce984b8a37570b6d7586c22c46f0196a08d10963")] // Missing prefix
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateSignature_MalformedHeader_ReturnsFalse(string? malformedHeader)
    {
        var secret = "test-secret";
        var body = Encoding.UTF8.GetBytes("test payload");

        var isValid = _validator.ValidateSignature(body, secret, malformedHeader);

        Assert.False(isValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void ValidateSignature_EmptySecret_ReturnsFalse(string? emptySecret)
    {
        var body = Encoding.UTF8.GetBytes("test payload");
        var isValid = _validator.ValidateSignature(body, emptySecret!, "sha256=something");

        Assert.False(isValid);
    }

    [Fact]
    public void ValidateSignature_EmptyBody_ComputesValidHmac()
    {
        var secret = "empty_body_secret";
        var body = Array.Empty<byte>();
        var header = ComputeHeader(secret, body);

        var isValid = _validator.ValidateSignature(body, secret, header);

        Assert.True(isValid);
    }
}
