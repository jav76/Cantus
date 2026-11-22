using Cantus.Infrastructure.Spotify;
using FluentAssertions;
using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

public sealed class SpotifyOptionsTests
{
    private const string SERVER_CLIENT_ID = "0123456789abcdef0123456789abcdef";

    [Theory]
    [InlineData("", null, true)]
    [InlineData("", true, true)]
    [InlineData("", false, false)]
    [InlineData(SERVER_CLIENT_ID, null, false)]
    [InlineData(SERVER_CLIENT_ID, true, true)]
    [InlineData(SERVER_CLIENT_ID, false, false)]
    public void UserClientIdsAllowed_DefaultsToOnlyWhenTheServerHasNoClientId(
        string serverClientId,
        bool? allowUserClientIds,
        bool expected)
    {
        SpotifyOptions options = new() { ClientId = serverClientId, AllowUserClientIds = allowUserClientIds };

        options.UserClientIdsAllowed.Should().Be(expected);
    }
}
