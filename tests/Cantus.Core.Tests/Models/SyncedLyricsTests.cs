using Cantus.Core.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Models;

public class SyncedLyricsTests
{
    [Fact]
    public void GetActiveLineIndex_WhenEmpty_ReturnsNegativeOne()
    {
        SyncedLyrics lyrics = new()
        {
            TrackId = "1",
            Title = "Song",
            Artist = "Artist",
            Lines = []
        };

        lyrics.GetActiveLineIndex(TimeSpan.FromSeconds(10)).Should().Be(-1);
        lyrics.GetActiveLine(TimeSpan.FromSeconds(10)).Should().BeNull();
    }

    [Fact]
    public void GetActiveLineIndex_BeforeFirstLine_ReturnsNegativeOne()
    {
        SyncedLyrics lyrics = new()
        {
            TrackId = "1",
            Title = "Song",
            Artist = "Artist",
            Lines =
            [
                new(TimeSpan.FromSeconds(10), "Line 1"),
                new(TimeSpan.FromSeconds(20), "Line 2"),
                new(TimeSpan.FromSeconds(30), "Line 3")
            ]
        };

        lyrics.GetActiveLineIndex(TimeSpan.FromSeconds(5)).Should().Be(-1);
        lyrics.GetActiveLine(TimeSpan.FromSeconds(5)).Should().BeNull();
    }

    [Fact]
    public void GetActiveLineIndex_ExactAndBetweenLines_ReturnsCorrectActiveLine()
    {
        SyncedLyrics lyrics = new()
        {
            TrackId = "1",
            Title = "Song",
            Artist = "Artist",
            Lines =
            [
                new(TimeSpan.FromSeconds(10), "Line 1"),
                new(TimeSpan.FromSeconds(20), "Line 2"),
                new(TimeSpan.FromSeconds(30), "Line 3")
            ]
        };

        // Exactly at Line 1
        lyrics.GetActiveLineIndex(TimeSpan.FromSeconds(10)).Should().Be(0);
        lyrics.GetActiveLine(TimeSpan.FromSeconds(10))!.Text.Should().Be("Line 1");

        // Between Line 1 and Line 2
        lyrics.GetActiveLineIndex(TimeSpan.FromSeconds(15)).Should().Be(0);
        lyrics.GetActiveLine(TimeSpan.FromSeconds(15))!.Text.Should().Be("Line 1");

        // Exactly at Line 2
        lyrics.GetActiveLineIndex(TimeSpan.FromSeconds(20)).Should().Be(1);
        lyrics.GetActiveLine(TimeSpan.FromSeconds(20))!.Text.Should().Be("Line 2");

        // Past Line 3
        lyrics.GetActiveLineIndex(TimeSpan.FromSeconds(50)).Should().Be(2);
        lyrics.GetActiveLine(TimeSpan.FromSeconds(50))!.Text.Should().Be("Line 3");
    }
}
