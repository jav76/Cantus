using System.Linq;
using System.Text.Json;
using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class LocalPlaybackPayloadJsonTests
{
    // The example from the wire contract shared with the server.
    private const string CONTRACT_UPDATE_JSON = """
        {
          "sequence": 42,
          "player": { "id": "spotify", "name": "Spotify" },
          "deviceName": "Spotify",
          "track": {
            "title": "Glimpse of Us",
            "artist": "Joji",
            "album": "SMITHEREENS",
            "durationMs": 233000,
            "albumArtUrl": "https://i.scdn.co/image/abc",
            "spotifyTrackId": "2mlNgAeIBnL78ZriXgrRHz"
          },
          "isPlaying": true,
          "positionMs": 98774,
          "rate": 1.0,
          "positionAtServerMs": 1795400000000,
          "capabilities": { "canPlay": true, "canPause": true, "canGoNext": true, "canGoPrevious": true, "canSeek": true }
        }
        """;

    [Fact]
    public void LocalPlaybackUpdate_SerializesWithTheContractFieldNames()
    {
        // Arrange
        LocalPlaybackUpdatePayload payload = JsonSerializer.Deserialize(
            CONTRACT_UPDATE_JSON,
            CantusJsonContext.Default.LocalPlaybackUpdatePayload)!;

        // Act
        string json = JsonSerializer.Serialize(payload, CantusJsonContext.Default.LocalPlaybackUpdatePayload);

        // Assert
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        PropertyNames(root).Should().Equal(
            "sequence", "player", "deviceName", "track", "isPlaying", "positionMs", "rate", "positionAtServerMs", "capabilities");
        PropertyNames(root.GetProperty("player")).Should().Equal("id", "name");
        PropertyNames(root.GetProperty("track")).Should().Equal(
            "title", "artist", "album", "durationMs", "albumArtUrl", "spotifyTrackId");
        PropertyNames(root.GetProperty("capabilities")).Should().Equal(
            "canPlay", "canPause", "canGoNext", "canGoPrevious", "canSeek");

        root.GetProperty("sequence").GetInt64().Should().Be(42);
        root.GetProperty("player").GetProperty("id").GetString().Should().Be("spotify");
        root.GetProperty("track").GetProperty("durationMs").GetInt64().Should().Be(233000);
        root.GetProperty("track").GetProperty("spotifyTrackId").GetString().Should().Be("2mlNgAeIBnL78ZriXgrRHz");
        root.GetProperty("positionMs").GetInt64().Should().Be(98774);
        root.GetProperty("rate").GetDouble().Should().Be(1.0);
        root.GetProperty("positionAtServerMs").GetInt64().Should().Be(1795400000000);
        root.GetProperty("capabilities").GetProperty("canSeek").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void LocalPlaybackUpdate_WithNothingToPublish_SendsExplicitNulls()
    {
        // Arrange
        LocalPlaybackUpdatePayload payload = new()
        {
            Sequence = 1,
            Player = new LocalPlayerInfoPayload { Id = "none", Name = "None" },
        };

        // Act
        using JsonDocument document = JsonDocument.Parse(
            JsonSerializer.Serialize(payload, CantusJsonContext.Default.LocalPlaybackUpdatePayload));

        // Assert
        document.RootElement.GetProperty("track").ValueKind.Should().Be(JsonValueKind.Null);
        document.RootElement.GetProperty("positionAtServerMs").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public void PlaybackState_ReadsTheLocalSourceAndCapabilities()
    {
        // Arrange
        const string JSON = """
            {
              "currentTrack": { "id": "local:0123456789abcdef0123456789abcdef", "title": "Ballin", "artist": "parkbreezy", "durationMs": 184000 },
              "progressMs": 1000,
              "isPlaying": true,
              "timestampUtc": "2026-11-23T20:00:00Z",
              "source": "Local",
              "capabilities": { "canPlay": true, "canPause": true, "canGoNext": false, "canGoPrevious": false, "canSeek": true }
            }
            """;

        // Act
        PlaybackStatePayload state = JsonSerializer.Deserialize(JSON, CantusJsonContext.Default.PlaybackStatePayload)!;

        // Assert
        state.Source.Should().Be(PlaybackStatePayload.SOURCE_LOCAL);
        state.Capabilities!.CanSeek.Should().BeTrue();
        state.Capabilities.CanGoNext.Should().BeFalse();
    }

    [Fact]
    public void PlaybackState_FromAServerWithoutTheNewFields_IsSpotify()
    {
        // Act
        PlaybackStatePayload state = JsonSerializer.Deserialize(
            """{ "progressMs": 1000, "isPlaying": false }""",
            CantusJsonContext.Default.PlaybackStatePayload)!;

        // Assert
        state.Source.Should().Be(PlaybackStatePayload.SOURCE_SPOTIFY);
        state.Capabilities.Should().BeNull();
    }

    [Fact]
    public void AuthorizedSession_ReadsALocalSessionWithoutASpotifyId()
    {
        // Arrange
        const string JSON = """
            { "id": "session-1", "spotifyUserId": null, "kind": "Local", "displayName": "jaret-desktop" }
            """;

        // Act
        AuthorizedSessionPayload session = JsonSerializer.Deserialize(JSON, CantusJsonContext.Default.AuthorizedSessionPayload)!;

        // Assert
        session.Kind.Should().Be(AuthorizedSessionPayload.KIND_LOCAL);
        session.IsLocal.Should().BeTrue();
        session.SpotifyUserId.Should().BeNull();
    }

    [Fact]
    public void AuthorizedSession_FromAServerWithoutKind_IsSpotify()
    {
        // Act
        AuthorizedSessionPayload session = JsonSerializer.Deserialize(
            """{ "id": "session-1", "spotifyUserId": "sp-1", "displayName": "Alice" }""",
            CantusJsonContext.Default.AuthorizedSessionPayload)!;

        // Assert
        session.Kind.Should().Be(AuthorizedSessionPayload.KIND_SPOTIFY);
        session.IsLocal.Should().BeFalse();
    }

    [Fact]
    public void LocalSessionRequest_SerializesTheContractBody()
    {
        // Act
        string json = JsonSerializer.Serialize(
            new LocalSessionRequestPayload { DisplayName = "jaret-desktop", JoinCode = null },
            CantusJsonContext.Default.LocalSessionRequestPayload);

        // Assert
        json.Should().Be("""{"displayName":"jaret-desktop","joinCode":null}""");
    }

    private static string[] PropertyNames(JsonElement element) =>
        element.EnumerateObject().Select(p => p.Name).ToArray();
}
