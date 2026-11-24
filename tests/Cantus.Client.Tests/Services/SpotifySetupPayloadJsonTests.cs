using System.Text.Json;
using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class SpotifySetupPayloadJsonTests
{
    [Fact]
    public void Deserialize_ReadsTheLocalSessionFields()
    {
        const string JSON = """
            {
              "serverClientIdConfigured": true,
              "userClientIdsAllowed": false,
              "redirectUri": "http://127.0.0.1:5000/api/auth/spotify/callback",
              "redirectUriAccepted": true,
              "dashboardUrl": "https://developer.spotify.com/dashboard/create",
              "localSessionsEnabled": true,
              "localSessionJoinCodeRequired": true
            }
            """;

        SpotifySetupInfoPayload? info = JsonSerializer.Deserialize(JSON, CantusJsonContext.Default.SpotifySetupInfoPayload);

        info!.LocalSessionsEnabled.Should().BeTrue();
        info.LocalSessionJoinCodeRequired.Should().BeTrue();
    }

    [Fact]
    public void Deserialize_FromAnOlderServer_LeavesLocalSessionsOff()
    {
        const string JSON = """{ "serverClientIdConfigured": true, "userClientIdsAllowed": true }""";

        SpotifySetupInfoPayload? info = JsonSerializer.Deserialize(JSON, CantusJsonContext.Default.SpotifySetupInfoPayload);

        info!.LocalSessionsEnabled.Should().BeFalse();
        info.LocalSessionJoinCodeRequired.Should().BeFalse();
    }

    [Fact]
    public void Serialize_UsesCamelCaseNames()
    {
        string json = JsonSerializer.Serialize(
            new SpotifySetupInfoPayload { LocalSessionsEnabled = true },
            CantusJsonContext.Default.SpotifySetupInfoPayload);

        json.Should().Contain("\"localSessionsEnabled\":true").And.Contain("\"localSessionJoinCodeRequired\":false");
    }
}
