using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Microsoft.UI.Xaml;
using Moq;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class LyricsPickerViewModelTests
{
    private const string TRACK_ID = "track-123";
    private const string OTHER_TRACK_ID = "track-456";

    private readonly Mock<ISignalRPlaybackClient> _client = new();
    private readonly Mock<ILyricsCorrectionService> _service = new();
    private readonly Mock<IDisplayManagementApi> _displaysApi = new();
    private readonly SessionViewModel _session;
    private TrackInfoPayload? _currentTrack = new()
    {
        Id = TRACK_ID,
        Title = "Miss America",
        Artist = "Brandon Flowers",
        DurationMs = 295000,
    };

    public LyricsPickerViewModelTests()
    {
        _displaysApi
            .Setup(a => a.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DisplayListResult(DisplayManagementStatus.Succeeded, new List<DisplayInfoPayload>()));
        _session = new SessionViewModel(_client.Object, _displaysApi.Object);
        _service
            .Setup(s => s.ApplyOverrideAsync(
                It.IsAny<string>(),
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsOverrideResult(LyricsCorrectionStatus.Succeeded, Lyrics(TRACK_ID)));
    }

    private LyricsPickerViewModel CreatePicker() => new(_service.Object, _session, () => _currentTrack);

    private void SignInAsOwner()
    {
        _session.ApplySessions(new List<AuthorizedSessionPayload> { new() { Id = "sess-owner", DisplayName = "Owner" } });
    }

    private static LyricsCandidatePayload Candidate(
        string provider,
        string id,
        bool isSynced = true,
        bool isInstrumental = false,
        long durationMs = 295011,
        params string[] preview) => new()
    {
        Provider = provider,
        ProviderCandidateId = id,
        Title = "Miss America",
        Artist = "Brandon Flowers",
        Album = "The Desired Effect",
        DurationMs = durationMs,
        IsSynced = isSynced,
        IsInstrumental = isInstrumental,
        Preview = preview.ToList(),
    };

    private static LyricsPayload Lyrics(string trackId) => new()
    {
        TrackId = trackId,
        IsSynced = true,
        Lines = new List<LyricLinePayload> { new() { TimestampMs = 1000, Text = "I'm writing you this letter" } },
    };

    private void ReturnCandidates(
        IEnumerable<LyricsCandidatePayload> candidates,
        IEnumerable<string>? unavailable = null,
        LyricsActiveOverridePayload? activeOverride = null)
    {
        LyricsCandidatesPayload payload = new()
        {
            TrackId = TRACK_ID,
            Candidates = candidates.ToList(),
            UnavailableProviders = (unavailable ?? Array.Empty<string>()).ToList(),
            ActiveOverride = activeOverride,
        };
        _service
            .Setup(s => s.GetCandidatesAsync(TRACK_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsCandidatesResult(LyricsCorrectionStatus.Succeeded, payload));
    }

    private void VerifyApplied(Func<LyricsOverrideRequestPayload, bool> match, Times times)
    {
        _service.Verify(
            s => s.ApplyOverrideAsync(
                TRACK_ID,
                It.Is<LyricsOverrideRequestPayload>(r => match(r)),
                It.IsAny<CancellationToken>()),
            times);
    }

    [Fact]
    public void WhenSignedOut_IsHidden()
    {
        // Arrange & Act
        LyricsPickerViewModel picker = CreatePicker();

        // Assert
        picker.IsAvailable.Should().BeFalse();
        picker.ActionVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void WithAnOwnerSessionAndATrack_IsShown()
    {
        // Arrange
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        SignInAsOwner();

        // Assert
        picker.IsAvailable.Should().BeTrue();
        picker.ActionVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public void WithNothingPlaying_IsHidden()
    {
        // Arrange
        _currentTrack = null;
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        SignInAsOwner();

        // Assert
        picker.IsAvailable.Should().BeFalse();
    }

    [Fact]
    public void OnADisplay_IsHiddenEvenThoughPlaybackAndDiagnosticsArrive()
    {
        // Arrange
        LyricsPickerViewModel picker = CreatePicker();

        // Act: a Display gets the owner's playback and diagnostics but never the Session list.
        _session.ApplyDiagnostics(new DiagnosticsPayload { AuthorizedSessions = 1, ActiveUserName = "Owner" });
        _session.ApplyActiveUser("Owner", null);
        picker.NotifyTrackChanged();

        // Assert
        _session.IsAuthorized.Should().BeTrue();
        picker.IsAvailable.Should().BeFalse();
        picker.ActionVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task OnADisplay_OpenAsyncRequestsNothing()
    {
        // Arrange
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        await picker.OpenAsync();

        // Assert
        picker.IsOpen.Should().BeFalse();
        _service.Verify(
            s => s.GetCandidatesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SigningOut_HidesAndClosesThePicker()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[] { Candidate("LRCLIB", "501") });
        LyricsPickerViewModel picker = CreatePicker();
        await picker.OpenAsync();
        List<string?> changed = new();
        picker.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        _session.ApplySessionRevoked(null);

        // Assert
        picker.IsAvailable.Should().BeFalse();
        picker.IsOpen.Should().BeFalse();
        changed.Should().Contain(nameof(LyricsPickerViewModel.ActionVisibility));
    }

    [Fact]
    public void TrackStartingAfterSignIn_ShowsTheAction()
    {
        // Arrange
        _currentTrack = null;
        SignInAsOwner();
        LyricsPickerViewModel picker = CreatePicker();
        List<string?> changed = new();
        picker.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        _currentTrack = new TrackInfoPayload { Id = TRACK_ID, Title = "Miss America", Artist = "Brandon Flowers" };
        picker.NotifyTrackChanged();

        // Assert
        picker.IsAvailable.Should().BeTrue();
        changed.Should().Contain(nameof(LyricsPickerViewModel.ActionVisibility));
    }

    [Fact]
    public async Task OpenAsync_ShowsLoadingUntilTheCandidatesArrive()
    {
        // Arrange
        SignInAsOwner();
        TaskCompletionSource<LyricsCandidatesResult> pending = new();
        _service
            .Setup(s => s.GetCandidatesAsync(TRACK_ID, It.IsAny<CancellationToken>()))
            .Returns(pending.Task);
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        Task opening = picker.OpenAsync();

        // Assert
        picker.IsOpen.Should().BeTrue();
        picker.IsLoading.Should().BeTrue();
        picker.LoadingVisibility.Should().Be(Visibility.Visible);
        picker.CanChoose.Should().BeFalse();
        picker.TrackText.Should().Be("Miss America · Brandon Flowers");

        pending.SetResult(new LyricsCandidatesResult(
            LyricsCorrectionStatus.Succeeded,
            new LyricsCandidatesPayload { TrackId = TRACK_ID, Candidates = new() { Candidate("LRCLIB", "501") } }));
        await opening;

        picker.IsLoading.Should().BeFalse();
        picker.LoadingVisibility.Should().Be(Visibility.Collapsed);
        picker.CanChoose.Should().BeTrue();
        picker.Candidates.Should().ContainSingle();
    }

    [Fact]
    public async Task OpenAsync_ListsEachCandidateWithItsDetailsAndPreview()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[]
        {
            Candidate("LRCLIB", "501", isSynced: true, preview: new[] { "I'm writing you this letter", "If you ain't figured out" }),
            Candidate("LRCLIB", "502", isSynced: false, durationMs: 61000, preview: new[] { "Plain line" }),
            Candidate("NetEase", "111", isSynced: false, isInstrumental: true, durationMs: 0),
        });
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        await picker.OpenAsync();

        // Assert
        picker.Candidates.Should().HaveCount(3);
        LyricsCandidateItem synced = picker.Candidates[0];
        synced.Provider.Should().Be("LRCLIB");
        synced.CandidateId.Should().Be("501");
        synced.Title.Should().Be("Miss America");
        synced.Artist.Should().Be("Brandon Flowers");
        synced.DurationText.Should().Be("4:55");
        synced.KindText.Should().Be("Synced");
        synced.DetailText.Should().Be("Brandon Flowers · 4:55 · Synced");
        synced.PreviewText.Should().Be("I'm writing you this letter\nIf you ain't figured out");
        synced.PreviewVisibility.Should().Be(Visibility.Visible);

        picker.Candidates[1].KindText.Should().Be("Plain");
        picker.Candidates[1].DurationText.Should().Be("1:01");

        LyricsCandidateItem instrumental = picker.Candidates[2];
        instrumental.KindText.Should().Be("Instrumental");
        instrumental.DurationText.Should().Be("--:--");
        instrumental.PreviewVisibility.Should().Be(Visibility.Collapsed);

        picker.StatusVisibility.Should().Be(Visibility.Collapsed);
        picker.UnavailableVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task OpenAsync_WithNoCandidates_SaysSoAndStillOffersTheOtherChoices()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(Array.Empty<LyricsCandidatePayload>());
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        await picker.OpenAsync();

        // Assert
        picker.Candidates.Should().BeEmpty();
        picker.StatusText.Should().Be("No other lyrics were found for this track.");
        picker.StatusVisibility.Should().Be(Visibility.Visible);
        picker.CanChoose.Should().BeTrue();
    }

    [Fact]
    public async Task OpenAsync_WhenAProviderFailed_ListsTheRestAndNamesIt()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[] { Candidate("NetEase", "111") }, unavailable: new[] { "LRCLIB" });
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        await picker.OpenAsync();

        // Assert
        picker.Candidates.Select(c => c.CandidateId).Should().Equal("111");
        picker.UnavailableText.Should().Be("LRCLIB could not be reached, so some lyrics may be missing.");
        picker.UnavailableVisibility.Should().Be(Visibility.Visible);
        picker.StatusVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task OpenAsync_WhenEveryProviderFailed_SaysNoneCouldBeReached()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(Array.Empty<LyricsCandidatePayload>(), unavailable: new[] { "LRCLIB", "NetEase" });
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        await picker.OpenAsync();

        // Assert
        picker.StatusText.Should().Be("No lyrics provider could be reached. Try again later.");
        picker.UnavailableText.Should().Be("LRCLIB and NetEase could not be reached, so some lyrics may be missing.");
    }

    [Theory]
    [InlineData(LyricsCorrectionStatus.Failed, "Could not reach the Cantus server. Try again.")]
    [InlineData(LyricsCorrectionStatus.NotFound, "This track is no longer playing.")]
    [InlineData(LyricsCorrectionStatus.SignedOut, "Sign in again to change lyrics.")]
    [InlineData(LyricsCorrectionStatus.NotAllowed, "Only the account owner can change lyrics.")]
    public async Task OpenAsync_WhenTheListCannotBeLoaded_ShowsWhy(LyricsCorrectionStatus status, string expected)
    {
        // Arrange
        SignInAsOwner();
        _service
            .Setup(s => s.GetCandidatesAsync(TRACK_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsCandidatesResult(status, null));
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        await picker.OpenAsync();

        // Assert
        picker.IsOpen.Should().BeTrue();
        picker.IsLoading.Should().BeFalse();
        picker.Candidates.Should().BeEmpty();
        picker.StatusText.Should().Be(expected);
    }

    [Fact]
    public async Task OpenAsync_AgainAfterAFailure_ReloadsTheList()
    {
        // Arrange
        SignInAsOwner();
        _service
            .Setup(s => s.GetCandidatesAsync(TRACK_ID, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsCandidatesResult(LyricsCorrectionStatus.Failed, null));
        LyricsPickerViewModel picker = CreatePicker();
        await picker.OpenAsync();
        ReturnCandidates(new[] { Candidate("LRCLIB", "501") });

        // Act
        await picker.OpenAsync();

        // Assert
        picker.Candidates.Should().ContainSingle();
        picker.StatusVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task OpenAsync_WithAnActiveCandidateOverride_MarksItAndOffersToClearIt()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(
            new[] { Candidate("LRCLIB", "501"), Candidate("NetEase", "111") },
            activeOverride: new LyricsActiveOverridePayload
            {
                Kind = LyricsActiveOverridePayload.KIND_CANDIDATE,
                Provider = "NetEase",
                CandidateId = "111",
            });
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        await picker.OpenAsync();

        // Assert
        picker.Candidates.Select(c => c.IsActive).Should().Equal(false, true);
        picker.Candidates[1].ActiveVisibility.Should().Be(Visibility.Visible);
        picker.Candidates[0].ActiveVisibility.Should().Be(Visibility.Collapsed);
        picker.HasActiveOverride.Should().BeTrue();
        picker.ActiveOverrideText.Should().Be("You picked these lyrics from NetEase.");
        picker.ClearOverrideVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public async Task OpenAsync_WithANoLyricsOverride_SaysSoAndOffersToClearIt()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(
            new[] { Candidate("LRCLIB", "501") },
            activeOverride: new LyricsActiveOverridePayload { Kind = LyricsActiveOverridePayload.KIND_NO_LYRICS });
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        await picker.OpenAsync();

        // Assert
        picker.Candidates.Should().OnlyContain(c => !c.IsActive);
        picker.ActiveOverrideText.Should().Be("You marked this track as having no lyrics.");
        picker.ClearOverrideVisibility.Should().Be(Visibility.Visible);
    }

    [Fact]
    public async Task OpenAsync_WithoutAnOverride_HidesTheClearChoice()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[] { Candidate("LRCLIB", "501") });
        LyricsPickerViewModel picker = CreatePicker();

        // Act
        await picker.OpenAsync();

        // Assert
        picker.HasActiveOverride.Should().BeFalse();
        picker.ClearOverrideVisibility.Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public async Task UseCandidateAsync_AppliesItRaisesTheNewLyricsAndCloses()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[] { Candidate("LRCLIB", "501"), Candidate("NetEase", "111") });
        LyricsPickerViewModel picker = CreatePicker();
        await picker.OpenAsync();
        List<LyricsPayload> raised = new();
        picker.LyricsChanged += raised.Add;

        // Act
        await picker.UseCandidateAsync(picker.Candidates[1]);

        // Assert
        VerifyApplied(
            r => r.Action == LyricsOverrideRequestPayload.ACTION_CANDIDATE
                && r.Provider == "NetEase"
                && r.CandidateId == "111",
            Times.Once());
        raised.Should().ContainSingle().Which.TrackId.Should().Be(TRACK_ID);
        picker.IsOpen.Should().BeFalse();
    }

    [Theory]
    [InlineData(LyricsOverrideRequestPayload.ACTION_REFETCH)]
    [InlineData(LyricsOverrideRequestPayload.ACTION_NO_LYRICS)]
    [InlineData(LyricsOverrideRequestPayload.ACTION_CLEAR)]
    public async Task EachOtherChoice_SendsItsActionRaisesTheNewLyricsAndCloses(string action)
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[] { Candidate("LRCLIB", "501") });
        LyricsPickerViewModel picker = CreatePicker();
        await picker.OpenAsync();
        List<LyricsPayload> raised = new();
        picker.LyricsChanged += raised.Add;

        // Act
        Task choice = action switch
        {
            LyricsOverrideRequestPayload.ACTION_REFETCH => picker.RefetchAsync(),
            LyricsOverrideRequestPayload.ACTION_NO_LYRICS => picker.MarkNoLyricsAsync(),
            _ => picker.ClearOverrideAsync(),
        };
        await choice;

        // Assert
        VerifyApplied(r => r.Action == action && r.Provider is null && r.CandidateId is null, Times.Once());
        raised.Should().ContainSingle();
        picker.IsOpen.Should().BeFalse();
    }

    [Theory]
    [InlineData(LyricsCorrectionStatus.ProviderUnavailable, "The lyrics provider could not be reached. Your lyrics are unchanged.")]
    [InlineData(LyricsCorrectionStatus.NotFound, "Those lyrics could not be loaded. The track may have changed.")]
    [InlineData(LyricsCorrectionStatus.Failed, "Could not reach the Cantus server. Try again.")]
    public async Task AFailedChoice_KeepsThePickerOpenAndShowsWhy(LyricsCorrectionStatus status, string expected)
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[] { Candidate("LRCLIB", "501") });
        _service
            .Setup(s => s.ApplyOverrideAsync(
                It.IsAny<string>(),
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LyricsOverrideResult(status, null));
        LyricsPickerViewModel picker = CreatePicker();
        await picker.OpenAsync();
        int raised = 0;
        picker.LyricsChanged += _ => raised++;

        // Act
        await picker.RefetchAsync();

        // Assert
        picker.IsOpen.Should().BeTrue();
        picker.IsApplying.Should().BeFalse();
        picker.CanChoose.Should().BeTrue();
        picker.StatusText.Should().Be(expected);
        picker.Candidates.Should().ContainSingle();
        raised.Should().Be(0);
    }

    [Fact]
    public async Task AChoiceInFlight_BlocksAnotherUntilItAnswers()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[] { Candidate("LRCLIB", "501") });
        TaskCompletionSource<LyricsOverrideResult> pending = new();
        _service
            .Setup(s => s.ApplyOverrideAsync(
                It.IsAny<string>(),
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(pending.Task);
        LyricsPickerViewModel picker = CreatePicker();
        await picker.OpenAsync();

        // Act
        Task first = picker.UseCandidateAsync(picker.Candidates[0]);
        bool canChooseWhileApplying = picker.CanChoose;
        await picker.MarkNoLyricsAsync();
        pending.SetResult(new LyricsOverrideResult(LyricsCorrectionStatus.Succeeded, Lyrics(TRACK_ID)));
        await first;

        // Assert
        canChooseWhileApplying.Should().BeFalse();
        _service.Verify(
            s => s.ApplyOverrideAsync(
                It.IsAny<string>(),
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ATrackChange_ClosesThePickerAndDropsTheLateList()
    {
        // Arrange
        SignInAsOwner();
        TaskCompletionSource<LyricsCandidatesResult> pending = new();
        _service
            .Setup(s => s.GetCandidatesAsync(TRACK_ID, It.IsAny<CancellationToken>()))
            .Returns(pending.Task);
        LyricsPickerViewModel picker = CreatePicker();
        Task opening = picker.OpenAsync();

        // Act
        _currentTrack = new TrackInfoPayload { Id = OTHER_TRACK_ID, Title = "Other", Artist = "Other" };
        picker.NotifyTrackChanged();
        pending.SetResult(new LyricsCandidatesResult(
            LyricsCorrectionStatus.Succeeded,
            new LyricsCandidatesPayload { TrackId = TRACK_ID, Candidates = new() { Candidate("LRCLIB", "501") } }));
        await opening;

        // Assert
        picker.IsOpen.Should().BeFalse();
        picker.Candidates.Should().BeEmpty();
        picker.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task ATrackChangeDuringAChoice_DoesNotRaiseThePreviousTracksLyrics()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[] { Candidate("LRCLIB", "501") });
        TaskCompletionSource<LyricsOverrideResult> pending = new();
        _service
            .Setup(s => s.ApplyOverrideAsync(
                It.IsAny<string>(),
                It.IsAny<LyricsOverrideRequestPayload>(),
                It.IsAny<CancellationToken>()))
            .Returns(pending.Task);
        LyricsPickerViewModel picker = CreatePicker();
        await picker.OpenAsync();
        int raised = 0;
        picker.LyricsChanged += _ => raised++;

        // Act
        Task choice = picker.UseCandidateAsync(picker.Candidates[0]);
        _currentTrack = new TrackInfoPayload { Id = OTHER_TRACK_ID, Title = "Other", Artist = "Other" };
        picker.NotifyTrackChanged();
        pending.SetResult(new LyricsOverrideResult(LyricsCorrectionStatus.Succeeded, Lyrics(TRACK_ID)));
        await choice;

        // Assert
        raised.Should().Be(0);
        picker.IsOpen.Should().BeFalse();
        picker.IsApplying.Should().BeFalse();
    }

    [Fact]
    public async Task Close_HidesThePicker()
    {
        // Arrange
        SignInAsOwner();
        ReturnCandidates(new[] { Candidate("LRCLIB", "501") });
        LyricsPickerViewModel picker = CreatePicker();
        await picker.OpenAsync();
        List<string?> changed = new();
        picker.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        // Act
        picker.Close();

        // Assert
        picker.IsOpen.Should().BeFalse();
        changed.Should().Contain(nameof(LyricsPickerViewModel.IsOpen));
    }
}
