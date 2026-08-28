using System;
using Cantus.Core.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Models;

public sealed class UserSessionTests
{
    [Fact]
    public void ToString_RedactsSensitiveTokens()
    {
        // Arrange
        UserSession session = new()
        {
            Id = "session-123",
            SpotifyUserId = "spotify-user-456",
            DisplayName = "Test User",
            Email = "user@example.com",
            AccessToken = "secret-access-token-abc",
            RefreshToken = "secret-refresh-token-xyz",
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        // Act
        string stringRep = session.ToString();

        // Assert
        stringRep.Should().Contain("Id = session-123");
        stringRep.Should().Contain("DisplayName = Test User");
        stringRep.Should().Contain("AccessToken = [REDACTED]");
        stringRep.Should().Contain("RefreshToken = [REDACTED]");
        stringRep.Should().NotContain("secret-access-token-abc");
        stringRep.Should().NotContain("secret-refresh-token-xyz");
    }
}
