using Cantus.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace Cantus.Infrastructure.Tests.Security;

public class DataProtectionTokenEncryptionServiceTests
{
    [Fact]
    public void EncryptAndDecrypt_ReturnsOriginalPlainText()
    {
        EphemeralDataProtectionProvider provider = new();
        DataProtectionTokenEncryptionService service = new(provider);
        string secretToken = "BQD3abc123_spotify_secret_token_value";

        string cipherText = service.Encrypt(secretToken);
        cipherText.Should().NotBeNullOrEmpty();
        cipherText.Should().NotBe(secretToken);

        string decrypted = service.Decrypt(cipherText);
        decrypted.Should().Be(secretToken);
    }

    [Fact]
    public void Encrypt_EmptyString_ReturnsEmptyString()
    {
        EphemeralDataProtectionProvider provider = new();
        DataProtectionTokenEncryptionService service = new(provider);

        service.Encrypt(string.Empty).Should().BeEmpty();
        service.Decrypt(string.Empty).Should().BeEmpty();
    }
}
