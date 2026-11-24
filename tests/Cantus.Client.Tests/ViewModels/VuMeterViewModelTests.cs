using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cantus.Client.Models;
using Cantus.Client.Services;
using Cantus.Client.Tests.Services;
using Cantus.Client.ViewModels;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.ViewModels;

public sealed class VuMeterViewModelTests
{
    private const string TRACK_ID = "track-a";
    private const long FRAME = VuMeterViewModel.FRAME_INTERVAL_MS;

    [Fact]
    public void Constructor_NoStoredSource_UsesTheProviderDefault()
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.SystemAudio);

        // Act
        VuMeterViewModel meter = new(Preferences(), provider);

        // Assert
        meter.SelectedSource.Should().Be(VuMeterSource.SystemAudio);
        meter.SelectedSourceIndex.Should().Be(1);
        meter.HasLiveSources.Should().BeTrue();
    }

    [Fact]
    public void Constructor_StoredSourceNotOnThisPlatform_FallsBackToTheDefault()
    {
        // Arrange
        LyricsPreferences preferences = Preferences();
        preferences.VuMeterSource = VuMeterSource.Microphone;

        // Act
        FakeProvider provider = new(defaultSource: VuMeterSource.SystemAudio)
        {
            AvailableSources = [VuMeterSource.Decorative, VuMeterSource.SystemAudio],
        };
        VuMeterViewModel meter = new(preferences, provider);

        // Assert
        meter.SelectedSource.Should().Be(VuMeterSource.SystemAudio);
    }

    [Fact]
    public void Constructor_WithoutAProvider_OffersDecorativeOnly()
    {
        // Act
        VuMeterViewModel meter = new(Preferences());

        // Assert
        meter.AvailableSources.Should().Equal(VuMeterSource.Decorative);
        meter.HasLiveSources.Should().BeFalse();
        meter.StatusText.Should().StartWith("Decorative");
    }

    [Fact]
    public void Update_PlayingWithoutLiveAudio_DrawsTheDecorativePattern()
    {
        // Arrange
        VuMeterViewModel meter = new(Preferences());
        int updates = 0;
        meter.LevelsUpdated += (s, e) => updates++;

        // Act
        meter.Update(1_000, TRACK_ID, 20_000, isPlaying: true);

        // Assert
        updates.Should().Be(1);
        meter.Mode.Should().Be(VuMeterMode.Decorative);
        Peak(meter).Should().BeGreaterThan(0);
    }

    [Fact]
    public void Update_CalledFasterThanTheFrameInterval_UpdatesOncePerInterval()
    {
        // Arrange
        VuMeterViewModel meter = new(Preferences());
        int updates = 0;
        meter.LevelsUpdated += (s, e) => updates++;

        // Act: the playback clock ticks every 16 ms.
        for (long now = 1_000; now < 1_000 + FRAME * 3; now += 16)
        {
            meter.Update(now, TRACK_ID, now * 10, isPlaying: true);
        }

        // Assert
        updates.Should().Be(3);
    }

    [Fact]
    public void Update_ClockJumpsBackwards_KeepsUpdating()
    {
        // Arrange
        VuMeterViewModel meter = new(Preferences());
        int updates = 0;
        meter.LevelsUpdated += (s, e) => updates++;
        meter.Update(10_000, TRACK_ID, 1_000, isPlaying: true);

        // Act
        meter.Update(5_000, TRACK_ID, 1_200, isPlaying: true);

        // Assert
        updates.Should().Be(2);
    }

    [Fact]
    public void Update_Paused_FallsToRestThenStopsRaisingUpdates()
    {
        // Arrange
        VuMeterViewModel meter = new(Preferences());
        long now = 1_000;
        meter.Update(now, TRACK_ID, 20_000, isPlaying: true);
        int updates = 0;
        meter.LevelsUpdated += (s, e) => updates++;

        // Act
        for (int frame = 0; frame < 200; frame++)
        {
            now += FRAME;
            meter.Update(now, TRACK_ID, 20_000, isPlaying: false);
        }

        int updatesWhileFalling = updates;
        for (int frame = 0; frame < 10; frame++)
        {
            now += FRAME;
            meter.Update(now, TRACK_ID, 20_000, isPlaying: false);
        }

        // Assert
        Peak(meter).Should().Be(0);
        updatesWhileFalling.Should().BeGreaterThan(1);
        updates.Should().Be(updatesWhileFalling);
    }

    [Fact]
    public void SetPointerOver_True_HoldsTheBarsStillUntilThePointerLeaves()
    {
        // Arrange
        VuMeterViewModel meter = new(Preferences());
        meter.Update(1_000, TRACK_ID, 20_000, isPlaying: true);
        float[] before = meter.Levels.ToArray();
        int updates = 0;
        meter.LevelsUpdated += (s, e) => updates++;

        // Act
        meter.SetPointerOver(true);
        meter.Update(1_000 + FRAME, TRACK_ID, 20_500, isPlaying: true);
        meter.Update(1_000 + FRAME * 2, TRACK_ID, 21_000, isPlaying: true);
        bool frozen = meter.IsFrozen;
        float[] during = meter.Levels.ToArray();
        meter.SetPointerOver(false);
        meter.Update(1_000 + FRAME * 3, TRACK_ID, 21_500, isPlaying: true);

        // Assert
        frozen.Should().BeTrue();
        during.Should().Equal(before);
        updates.Should().Be(1);
        meter.IsFrozen.Should().BeFalse();
        meter.Levels.ToArray().Should().NotEqual(before);
    }

    [Fact]
    public async Task Update_LiveSourceHearsSound_ShowsTheLiveLevels()
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.SystemAudio);
        VuMeterViewModel meter = new(Preferences(), provider);
        await meter.ActivateAsync();
        FakeSource source = provider.Created[0];
        source.Bands.AsSpan().Fill(0.8f);

        // Act
        for (long now = 1_000; now < 1_000 + FRAME * 20; now += FRAME)
        {
            meter.Update(now, TRACK_ID, 20_000, isPlaying: true);
        }

        // Assert
        meter.Mode.Should().Be(VuMeterMode.Live);
        meter.StatusText.Should().Be("Live: levels from system audio.");
        meter.Levels.ToArray().Should().OnlyContain(level => Math.Abs(level - 0.8f) < 0.01f);
        source.StartedByUser.Should().BeFalse();
    }

    [Fact]
    public async Task Update_LiveSourceSilentTooLong_FallsBackToDecorative()
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.SystemAudio);
        VuMeterViewModel meter = new(Preferences(), provider);
        await meter.ActivateAsync();
        FakeSource source = provider.Created[0];
        source.Bands.AsSpan().Fill(0.8f);
        long now = 1_000;
        meter.Update(now, TRACK_ID, 20_000, isPlaying: true);

        // Act
        source.Bands.AsSpan().Clear();
        while (now < 1_000 + VuMeterViewModel.SILENCE_FALLBACK_MS + FRAME)
        {
            now += FRAME;
            meter.Update(now, TRACK_ID, 20_000, isPlaying: true);
        }

        // Assert
        meter.Mode.Should().Be(VuMeterMode.Decorative);
        meter.StatusText.Should().StartWith("Waiting for sound from system audio");
        Peak(meter).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Update_LiveSourceStalls_FallsBackToDecorative()
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.SystemAudio);
        VuMeterViewModel meter = new(Preferences(), provider);
        await meter.ActivateAsync();
        FakeSource source = provider.Created[0];
        source.Bands.AsSpan().Fill(0.8f);
        long now = 1_000;
        meter.Update(now, TRACK_ID, 20_000, isPlaying: true);

        // Act
        source.HasData = false;
        while (now < 1_000 + VuMeterViewModel.STALL_MS + FRAME)
        {
            now += FRAME;
            meter.Update(now, TRACK_ID, 20_000, isPlaying: true);
        }

        // Assert
        meter.Mode.Should().Be(VuMeterMode.Decorative);
    }

    [Fact]
    public async Task Update_LiveSourceEnds_DisposesItAndSaysHowToResume()
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.SharedAudio);
        VuMeterViewModel meter = new(Preferences(), provider);
        meter.SelectSource(VuMeterSource.SharedAudio);
        await meter.PendingStart!;
        FakeSource source = provider.Created[^1];

        // Act
        source.IsActive = false;
        meter.Update(1_000, TRACK_ID, 20_000, isPlaying: true);

        // Assert
        source.IsDisposed.Should().BeTrue();
        meter.Mode.Should().Be(VuMeterMode.Decorative);
        meter.StatusText.Should().StartWith("Tab or screen audio stopped. Pick it again");
    }

    [Theory]
    [InlineData(AudioLevelSourceStartResult.Denied, "Microphone access was denied")]
    [InlineData(AudioLevelSourceStartResult.Unavailable, "Microphone is not available")]
    [InlineData(AudioLevelSourceStartResult.NeedsUserAction, "Pick microphone again")]
    public async Task ActivateAsync_SourceDoesNotStart_ExplainsWhyAndDisposesIt(
        AudioLevelSourceStartResult result,
        string expectedStatus)
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.Microphone) { StartResult = result };
        VuMeterViewModel meter = new(Preferences(), provider);

        // Act
        await meter.ActivateAsync();

        // Assert
        meter.StatusText.Should().StartWith(expectedStatus);
        provider.Created[0].IsDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task ActivateAsync_SourceThrows_ReportsItUnavailable()
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.SystemAudio) { ThrowOnStart = true };
        VuMeterViewModel meter = new(Preferences(), provider);

        // Act
        await meter.ActivateAsync();

        // Assert
        meter.StatusText.Should().StartWith("System audio is not available");
    }

    [Fact]
    public async Task SelectSource_StartsFromAClickAndPersistsTheChoice()
    {
        // Arrange
        LyricsPreferences preferences = Preferences();
        FakeProvider provider = new(defaultSource: VuMeterSource.Decorative);
        VuMeterViewModel meter = new(preferences, provider);
        List<string?> changed = [];
        meter.PropertyChanged += (s, e) => changed.Add(e.PropertyName);

        // Act
        meter.SelectedSourceIndex = 2;
        await meter.PendingStart!;

        // Assert
        meter.SelectedSource.Should().Be(VuMeterSource.Microphone);
        preferences.VuMeterSource.Should().Be(VuMeterSource.Microphone);
        provider.Created.Should().ContainSingle().Which.StartedByUser.Should().BeTrue();
        changed.Should().Contain([nameof(VuMeterViewModel.SelectedSource), nameof(VuMeterViewModel.SelectedSourceIndex)]);
    }

    [Fact]
    public async Task SelectSource_WhileAnotherStartIsPending_DropsTheOlderSource()
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.Microphone) { HoldStart = true };
        VuMeterViewModel meter = new(Preferences(), provider);
        Task first = meter.ActivateAsync();

        // Act
        provider.HoldStart = false;
        meter.SelectSource(VuMeterSource.SharedAudio);
        await meter.PendingStart!;
        await first;

        // Assert
        provider.Created[0].IsDisposed.Should().BeTrue();
        provider.Created[1].IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void SelectSource_NotOnThisPlatform_IsIgnored()
    {
        // Arrange
        VuMeterViewModel meter = new(Preferences());

        // Act
        meter.SelectSource(VuMeterSource.Microphone);

        // Assert
        meter.SelectedSource.Should().Be(VuMeterSource.Decorative);
    }

    [Fact]
    public async Task IsEnabled_False_StopsTheSourceAndClearsTheBars()
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.SystemAudio);
        VuMeterViewModel meter = new(Preferences(), provider);
        await meter.ActivateAsync();
        meter.Update(1_000, TRACK_ID, 20_000, isPlaying: true);

        // Act
        meter.IsEnabled = false;
        meter.Update(1_000 + FRAME, TRACK_ID, 20_500, isPlaying: true);

        // Assert
        provider.Created[0].IsDisposed.Should().BeTrue();
        Peak(meter).Should().Be(0);
        meter.StatusText.Should().Be("Off.");
    }

    [Fact]
    public async Task IsEnabled_TurnedBackOn_RestartsTheSource()
    {
        // Arrange
        FakeProvider provider = new(defaultSource: VuMeterSource.SystemAudio);
        VuMeterViewModel meter = new(Preferences(), provider);
        await meter.ActivateAsync();
        meter.IsEnabled = false;

        // Act
        meter.IsEnabled = true;
        await meter.PendingStart!;

        // Assert
        provider.Created.Should().HaveCount(2);
        provider.Created[1].IsDisposed.Should().BeFalse();
    }

    [Fact]
    public void Constructor_StoredDisabled_DoesNotUpdate()
    {
        // Arrange
        LyricsPreferences preferences = Preferences();
        preferences.IsVuMeterEnabled = false;
        VuMeterViewModel meter = new(preferences);
        int updates = 0;
        meter.LevelsUpdated += (s, e) => updates++;

        // Act
        meter.Update(1_000, TRACK_ID, 20_000, isPlaying: true);

        // Assert
        meter.IsEnabled.Should().BeFalse();
        updates.Should().Be(0);
    }

    [Fact]
    public void SampleLevel_InterpolatesBetweenBands()
    {
        // Arrange
        VuMeterViewModel meter = new(Preferences());
        meter.Update(1_000, TRACK_ID, 20_000, isPlaying: true);
        float first = meter.Levels[0];
        float second = meter.Levels[1];
        double betweenFirstAndSecond = 0.5 / (VuMeterViewModel.BAND_COUNT - 1);

        // Act / Assert
        meter.SampleLevel(0).Should().Be(first);
        meter.SampleLevel(-1).Should().Be(first);
        meter.SampleLevel(2).Should().Be(meter.Levels[VuMeterViewModel.BAND_COUNT - 1]);
        meter.SampleLevel(betweenFirstAndSecond).Should().BeApproximately((first + second) / 2, 1e-5f);
    }

    private static LyricsPreferences Preferences() => new(new InMemorySettingsStore());

    private static float Peak(VuMeterViewModel meter)
    {
        float peak = 0;
        foreach (float level in meter.Levels)
        {
            peak = Math.Max(peak, level);
        }

        return peak;
    }

    private sealed class FakeProvider : IAudioLevelSourceProvider
    {
        public FakeProvider(VuMeterSource defaultSource)
        {
            DefaultSource = defaultSource;
        }

        public IReadOnlyList<VuMeterSource> AvailableSources { get; init; } =
            [VuMeterSource.Decorative, VuMeterSource.SystemAudio, VuMeterSource.Microphone, VuMeterSource.SharedAudio];

        public VuMeterSource DefaultSource { get; }

        public AudioLevelSourceStartResult StartResult { get; init; } = AudioLevelSourceStartResult.Started;

        public bool ThrowOnStart { get; init; }

        public bool HoldStart { get; set; }

        public List<FakeSource> Created { get; } = [];

        public IAudioLevelSource? Create(VuMeterSource source)
        {
            FakeSource created = new(StartResult, ThrowOnStart, HoldStart);
            Created.Add(created);
            return created;
        }
    }

    private sealed class FakeSource : IAudioLevelSource
    {
        private readonly AudioLevelSourceStartResult _result;
        private readonly bool _throwOnStart;
        private readonly bool _holdStart;

        public FakeSource(AudioLevelSourceStartResult result, bool throwOnStart, bool holdStart)
        {
            _result = result;
            _throwOnStart = throwOnStart;
            _holdStart = holdStart;
        }

        public float[] Bands { get; } = new float[VuMeterViewModel.BAND_COUNT];

        public bool HasData { get; set; } = true;

        public bool IsActive { get; set; } = true;

        public bool IsDisposed { get; private set; }

        public bool? StartedByUser { get; private set; }

        public async Task<AudioLevelSourceStartResult> StartAsync(bool isUserInitiated, CancellationToken cancellationToken)
        {
            StartedByUser = isUserInitiated;
            if (_throwOnStart)
            {
                throw new InvalidOperationException("no device");
            }

            if (_holdStart)
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }

            return _result;
        }

        public bool TryReadBands(Span<float> bands)
        {
            if (!HasData)
            {
                return false;
            }

            Bands.CopyTo(bands);
            return true;
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
