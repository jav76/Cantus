using System.Text.Json;
using Cantus.Client.Services;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Services;

public sealed class LyricsPayloadJsonTests
{
    private const string LYRICS_JSON = """
        {
          "trackId": "4cOdK2wGLETKBW3PvgPWqT",
          "title": "Song",
          "artist": "Artist",
          "isSynced": true,
          "isInstrumental": false,
          "lines": [
            {
              "timestampMs": 18450,
              "text": "We're no strangers",
              "syllables": [
                { "timestampMs": 18450, "durationMs": 420, "text": "We're " },
                { "timestampMs": 18870, "durationMs": 310, "text": "no " },
                { "timestampMs": 19180, "durationMs": 900, "text": "strangers" }
              ]
            },
            {
              "timestampMs": 22800,
              "text": "You know the rules and so do I",
              "syllables": null
            }
          ]
        }
        """;

    [Fact]
    public void SourceGeneratedContext_ReadsSyllablesAndKeepsTrailingSpaces()
    {
        // Act - the same source-generated metadata the trimmed WASM head uses
        LyricsPayload? payload = JsonSerializer.Deserialize(LYRICS_JSON, CantusJsonContext.Default.LyricsPayload);

        // Assert
        payload.Should().NotBeNull();
        payload!.Lines.Should().HaveCount(2);

        LyricLinePayload wordTimed = payload.Lines[0];
        wordTimed.Syllables.Should().NotBeNull();
        wordTimed.Syllables!.Should().HaveCount(3);
        wordTimed.Syllables[0].TimestampMs.Should().Be(18450);
        wordTimed.Syllables[0].DurationMs.Should().Be(420);
        wordTimed.Syllables[0].Text.Should().Be("We're ");
        wordTimed.Syllables[2].Text.Should().Be("strangers");

        payload.Lines[1].Syllables.Should().BeNull();
    }

    [Fact]
    public void SourceGeneratedContext_LineWithoutSyllablesField_HasNoSyllables()
    {
        // Arrange - an older server omits the field entirely
        const string json = """{ "trackId": "t", "lines": [ { "timestampMs": 1000, "text": "a line" } ] }""";

        // Act
        LyricsPayload? payload = JsonSerializer.Deserialize(json, CantusJsonContext.Default.LyricsPayload);

        // Assert
        payload!.Lines[0].Syllables.Should().BeNull();
    }
}
