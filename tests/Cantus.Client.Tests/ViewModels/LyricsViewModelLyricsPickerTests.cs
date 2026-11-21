using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricsViewModelLyricsPickerTests
{
    private const string TRACK_ID = "track-123";

    private readonly SignalRPlaybackClient _client = new();
    private readonly Mock<ILyricsCorrectionService> _service = new();
    private readonly Mock<IDisplayManagementApi> _displaysApi = new();

    public LyricsViewModelLyricsPickerTests()
    {
        _displaysApi
            .Setup(a => a.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayListResult(DisplayManagementStatus.Succeeded, new List<DisplayInfoPayload>()));
    }

    private LyricsViewModel CreateRoot() => new(
        _client,
        displayCredentials: new DisplayCredentialStore(new InMemorySettingsStore()),
        displaysApi: _displaysApi.Object,
        pendingPairCodes: new PendingPairCodeStore(new InMemorySettingsStore()),
        lyricsCorrection: _service.Object);

    private void PlayTrack(string trackId)
    {
        _client.RaisePlaybackStateReceived(new PlaybackStatePayload
        {
            CurrentTrack = new TrackInfoPayload
            {
                Id = trackId,
                Title = "Miss America",
                Artist = "Brandon Flowers",
                DurationMs = 295000,
            },
            IsPlaying = true,
        });
    }

    private void SignInAsOwner()
    {
        _client.RaiseSessionsReceived(new List<AuthorizedSessionPayload>
        {
            new() { Id = "sess-owner", DisplayName = "Owner" },
        });
    }

    private static LyricsPayload Lyrics(string trackId, params string[] lines) => new()
    {
        TrackId = trackId,
        IsSynced = true,
        Lines = lines.Select((text, i) => new LyricLinePayload { TimestampMs = (i + 1) * 1000, Text = text }).ToList(),
    };

    private void ReturnOneCandidate()
    {
        _service
            .Setup(s => s.GetCandidatesAsync(TRACK_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsCandidatesResult(
                LyricsCorrectionStatus.Succeeded,
                new LyricsCandidatesPayload
                {
                    TrackId = TRACK_ID,
                    Candidates = new()
                    {
                        new() { Provider = "NetEase", ProviderCandidateId = "111", Title = "Miss America" },
                    },
                }));
    }

    [Fact]
    public void ThePicker_FollowsTheOwnersSessionAndCurrentTrack()
    {
        // Arrange
        LyricsViewModel root = CreateRoot();

        // Act & Assert
        root.LyricsPicker.ActionVisibility.Should().Be(Visibility.Collapsed);

        PlayTrack(TRACK_ID);
        root.LyricsPicker.ActionVisibility.Should().Be(Visibility.Collapsed, "nobody is signed in");

        SignInAsOwner();
        root.LyricsPicker.ActionVisibility.Should().Be(Visibility.Visible);

        _client.RaiseSessionRevoked("sess-owner");
        root.LyricsPicker.ActionVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task SelectingACandidate_ReplacesTheLyricsWithoutAReload()
    {
        // Arrange
        LyricsViewModel root = CreateRoot();
        SignInAsOwner();
        PlayTrack(TRACK_ID);
        _client.RaiseLyricsReceived(Lyrics(TRACK_ID, "Wrong first line", "Wrong second line"));
        ReturnOneCandidate();
        _service
            .Setup(s => s.ApplyOverrideAsync(
                TRACK_ID,
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsOverrideResult(
                LyricsCorrectionStatus.Succeeded,
                Lyrics(TRACK_ID, "I'm writing you this letter")));
        int reloads = 0;
        root.LyricsReloaded += () => reloads++;

        // Act
        await root.LyricsPicker.OpenAsync();
        await root.LyricsPicker.UseCandidateAsync(root.LyricsPicker.Candidates[0]);

        // Assert
        root.LyricLines.Select(l => l.Text).Should().Equal("I'm writing you this letter");
        root.HasLyrics.Should().BeTrue();
        reloads.Should().Be(1);
        root.LyricsPicker.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task SelectingWordTimedLyrics_KeepsTheirSyllablesForTheKaraokeFill()
    {
        // Arrange
        LyricsViewModel root = CreateRoot();
        SignInAsOwner();
        PlayTrack(TRACK_ID);
        _client.RaiseLyricsReceived(Lyrics(TRACK_ID, "Wrong first line"));
        ReturnOneCandidate();
        LyricsPayload wordTimed = new()
        {
            TrackId = TRACK_ID,
            IsSynced = true,
            Lines = new List<LyricLinePayload>
            {
                new()
                {
                    TimestampMs = 1000,
                    Text = "I'm writing",
                    Syllables = new List<LyricSyllablePayload>
                    {
                        new() { TimestampMs = 1000, DurationMs = 300, Text = "I'm " },
                        new() { TimestampMs = 1300, DurationMs = 700, Text = "writing" },
                    },
                },
                new() { TimestampMs = 4000, Text = "you this letter" },
            },
        };
        _service
            .Setup(s => s.ApplyOverrideAsync(
                TRACK_ID,
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsOverrideResult(LyricsCorrectionStatus.Succeeded, wordTimed));

        // Act
        await root.LyricsPicker.OpenAsync();
        await root.LyricsPicker.UseCandidateAsync(root.LyricsPicker.Candidates[0]);

        // Assert
        root.LyricLines.Should().HaveCount(2);
        root.LyricLines[0].Syllables.Should().NotBeNull();
        root.LyricLines[0].Syllables!.Select(s => (s.TimestampMs, s.DurationMs, s.Text)).Should().Equal(
            (1000L, 300L, "I'm "),
            (1300L, 700L, "writing"));
        root.LyricLines[1].Syllables.Should().BeNull();
    }

    [Fact]
    public async Task AChoiceAnsweringAfterTheNextTracksLyrics_KeepsTheNextTracksLyrics()
    {
        // Arrange
        LyricsViewModel root = CreateRoot();
        SignInAsOwner();
        PlayTrack(TRACK_ID);
        _client.RaiseLyricsReceived(Lyrics(TRACK_ID, "Wrong first line"));
        ReturnOneCandidate();
        TaskCompletionSource<LyricsOverrideResult> answer = new();
        _service
            .Setup(s => s.ApplyOverrideAsync(
                TRACK_ID,
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(answer.Task);
        await root.LyricsPicker.OpenAsync();
        Task choice = root.LyricsPicker.UseCandidateAsync(root.LyricsPicker.Candidates[0]);

        // Act
        _client.RaiseLyricsReceived(Lyrics("track-456", "Next track's first line"));
        answer.SetResult(new LyricsOverrideResult(
            LyricsCorrectionStatus.Succeeded,
            Lyrics(TRACK_ID, "I'm writing you this letter")));
        await choice;
        PlayTrack("track-456");

        // Assert
        root.LyricLines.Select(l => l.Text).Should().Equal("Next track's first line");
    }

    [Fact]
    public async Task AChoiceAnsweringAfterTheTrackChanged_IsNotShown()
    {
        // Arrange
        LyricsViewModel root = CreateRoot();
        SignInAsOwner();
        PlayTrack(TRACK_ID);
        ReturnOneCandidate();
        _service
            .Setup(s => s.ApplyOverrideAsync(
                TRACK_ID,
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsOverrideResult(
                LyricsCorrectionStatus.Succeeded,
                Lyrics("track-456", "Lyrics for another track")));
        await root.LyricsPicker.OpenAsync();

        // Act
        await root.LyricsPicker.UseCandidateAsync(root.LyricsPicker.Candidates[0]);

        // Assert
        root.LyricLines.Should().BeEmpty();
    }

    [Fact]
    public async Task MarkingNoLyrics_EmptiesTheLyricsStage()
    {
        // Arrange
        LyricsViewModel root = CreateRoot();
        SignInAsOwner();
        PlayTrack(TRACK_ID);
        _client.RaiseLyricsReceived(Lyrics(TRACK_ID, "Wrong first line"));
        ReturnOneCandidate();
        _service
            .Setup(s => s.ApplyOverrideAsync(
                TRACK_ID,
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsOverrideResult(LyricsCorrectionStatus.Succeeded, Lyrics(TRACK_ID)));

        // Act
        await root.LyricsPicker.OpenAsync();
        await root.LyricsPicker.MarkNoLyricsAsync();

        // Assert
        root.LyricLines.Should().BeEmpty();
        root.HasLyrics.Should().BeFalse();
    }

    [Fact]
    public async Task ANewTrack_ClosesAnOpenPicker()
    {
        // Arrange
        LyricsViewModel root = CreateRoot();
        SignInAsOwner();
        PlayTrack(TRACK_ID);
        ReturnOneCandidate();
        await root.LyricsPicker.OpenAsync();

        // Act
        PlayTrack("track-456");

        // Assert
        root.LyricsPicker.IsOpen.Should().BeFalse();
        root.LyricsPicker.IsAvailable.Should().BeTrue();
    }
}
