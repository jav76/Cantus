using System.Text.Json;
using Cantus.Core.Interfaces;
using Cantus.Core.Models;
using Cantus.Core.Search;
using Cantus.Infrastructure.Persistence.Entities;
using Cantus.Infrastructure.Sessions;
using Cantus.Server.Hubs;
using Cantus.Server.Models;
using Cantus.Server.Services;
using Cantus.Server.Tests.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Cantus.Server.Tests.Hubs;

/// <summary>
/// <c>PublishLocalPlayback</c> over real SignalR connections: only a Local Session's own
/// connection may publish, and the server's echo reaches the owner and their Displays the same
/// way polled Spotify playback does, without the Session id for Displays.
/// </summary>
public sealed class PlaybackHubLocalPublishTests : IClassFixture<WebApplicationFactory<Program>>, IAsyncLifetime
{
    private const string PUBLISH = nameof(PlaybackHub.PublishLocalPlayback);
    private const int PUBLISHER_GRACE_SECONDS = 20;

    private readonly WebApplicationFactory<Program> _factory;
    private readonly DisplayTestHost _host;

    public PlaybackHubLocalPublishTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
        _host = new DisplayTestHost(
            factory,
            configureServices: services =>
            {
                services.AddScoped(_ => Mock.Of<ILyricsProvider>());
                services.Configure<LocalSessionOptions>(options => options.Enabled = true);
            });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _host.DisposeAsync().AsTask();

    [Fact]
    public async Task Publish_FromLocalSession_IsAcceptedAndEchoedToOwnerAndDisplay()
    {
        UserSessionEntity local = await _host.Store.SeedLocalSessionAsync("Desktop");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(local.Id, "TV");
        HubConnection owner = await _host.ConnectAsync(local.Id);
        HubConnection display = await _host.ConnectDisplayAsync(tv);
        Task<PlaybackStateDto> ownerState = NextStateWithTrackAsync(owner);
        Task<PlaybackStateDto> displayState = NextStateWithTrackAsync(display);
        Task<LyricsDto> ownerLyrics = NextAsync<LyricsDto>(owner, nameof(IPlaybackClient.ReceiveLyrics));
        Task<LyricsDto> displayLyrics = NextAsync<LyricsDto>(display, nameof(IPlaybackClient.ReceiveLyrics));

        int result = await owner.InvokeAsync<int>(PUBLISH, Update(1));

        result.Should().Be((int)LocalPublishResult.Accepted);
        string expectedTrackId = TrackIdentity.DeriveLocalKey("Glimpse of Us", "Joji", TimeSpan.FromMilliseconds(233_000));

        PlaybackStateDto ownerEcho = await DisplayTestHost.WaitAsync(ownerState);
        ownerEcho.Source.Should().Be(PlaybackStateDto.SOURCE_LOCAL);
        ownerEcho.ActiveUserId.Should().Be(local.Id);
        ownerEcho.ActiveUserDisplayName.Should().Be("Desktop");
        ownerEcho.CurrentTrack!.Id.Should().Be(expectedTrackId);
        ownerEcho.Capabilities!.CanSeek.Should().BeTrue();

        PlaybackStateDto displayEcho = await DisplayTestHost.WaitAsync(displayState);
        displayEcho.ActiveUserId.Should().BeNull("a Display must never see the owner's Session id");
        displayEcho.ActiveUserDisplayName.Should().Be("Desktop");
        displayEcho.Source.Should().Be(PlaybackStateDto.SOURCE_LOCAL);
        displayEcho.CurrentTrack!.Id.Should().Be(expectedTrackId);

        (await DisplayTestHost.WaitAsync(ownerLyrics)).TrackId.Should().Be(expectedTrackId);
        (await DisplayTestHost.WaitAsync(displayLyrics)).TrackId.Should().Be(expectedTrackId);
    }

    [Fact]
    public async Task Publish_OfTheContractSampleJson_IsAcceptedAndEchoedWithTheContractFieldNames()
    {
        const string SAMPLE = """
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
              "positionAtServerMs": null,
              "capabilities": { "canPlay": true, "canPause": true, "canGoNext": true, "canGoPrevious": false, "canSeek": true }
            }
            """;
        UserSessionEntity local = await _host.Store.SeedLocalSessionAsync("Desktop");
        HubConnection owner = await _host.ConnectAsync(local.Id);
        Task<JsonElement> echo = NextAsync<JsonElement>(
            owner,
            nameof(IPlaybackClient.ReceivePlaybackState),
            state => state.TryGetProperty("currentTrack", out JsonElement track) && track.ValueKind == JsonValueKind.Object);

        using JsonDocument sample = JsonDocument.Parse(SAMPLE);
        int result = await owner.InvokeAsync<int>(PUBLISH, sample.RootElement);

        result.Should().Be(0, "0 is Accepted on the wire");
        JsonElement state = await DisplayTestHost.WaitAsync(echo);
        state.GetProperty("source").GetString().Should().Be("Local");
        state.GetProperty("isPlaying").GetBoolean().Should().BeTrue();
        JsonElement track = state.GetProperty("currentTrack");
        track.GetProperty("id").GetString().Should().StartWith("local:");
        track.GetProperty("title").GetString().Should().Be("Glimpse of Us");
        track.GetProperty("artist").GetString().Should().Be("Joji");
        JsonElement capabilities = state.GetProperty("capabilities");
        capabilities.GetProperty("canSeek").GetBoolean().Should().BeTrue();
        capabilities.GetProperty("canGoNext").GetBoolean().Should().BeTrue();
        capabilities.GetProperty("canGoPrevious").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Publish_WhenLocalSessionsAreDisabled_IsRefusedForAnExistingLocalSession()
    {
        await using DisplayTestHost disabled = new(
            _factory,
            configureServices: services => services.AddScoped(_ => Mock.Of<ILyricsProvider>()));
        UserSessionEntity local = await disabled.Store.SeedLocalSessionAsync("Desktop");
        HubConnection owner = await disabled.ConnectAsync(local.Id);

        int result = await owner.InvokeAsync<int>(PUBLISH, Update(1));

        result.Should().Be((int)LocalPublishResult.NotLocalSession);
    }

    [Fact]
    public async Task Publish_FromDisplayConnection_IsRefused()
    {
        UserSessionEntity local = await _host.Store.SeedLocalSessionAsync("Desktop");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(local.Id, "TV");
        HubConnection display = await _host.ConnectDisplayAsync(tv);

        int result = await display.InvokeAsync<int>(PUBLISH, Update(1));

        result.Should().Be((int)LocalPublishResult.NotLocalSession);
    }

    [Fact]
    public async Task Publish_FromSpotifySession_IsRefused()
    {
        UserSessionEntity spotify = await _host.Store.SeedSessionAsync("Owner");
        HubConnection owner = await _host.ConnectAsync(spotify.Id);

        int result = await owner.InvokeAsync<int>(PUBLISH, Update(1));

        result.Should().Be((int)LocalPublishResult.NotLocalSession);
    }

    [Fact]
    public async Task Publish_FromAnonymousConnection_IsRefused()
    {
        HubConnection anonymous = await _host.ConnectAsync();

        int result = await anonymous.InvokeAsync<int>(PUBLISH, Update(1));

        result.Should().Be((int)LocalPublishResult.NotLocalSession);
    }

    [Fact]
    public async Task Publish_FromANewerConnectionOfTheSession_TakesTheClaimFromTheOlderOne()
    {
        UserSessionEntity local = await _host.Store.SeedLocalSessionAsync("Desktop");
        HubConnection first = await _host.ConnectAsync(local.Id);
        HubConnection second = await _host.ConnectAsync(local.Id);
        (await first.InvokeAsync<int>(PUBLISH, Update(1))).Should().Be((int)LocalPublishResult.Accepted);

        (await second.InvokeAsync<int>(PUBLISH, Update(1))).Should().Be((int)LocalPublishResult.Accepted);

        (await first.InvokeAsync<int>(PUBLISH, Update(2))).Should().Be((int)LocalPublishResult.NotPublisher);
    }

    [Fact]
    public async Task Publish_WithInvalidTrack_IsInvalid()
    {
        UserSessionEntity local = await _host.Store.SeedLocalSessionAsync("Desktop");
        HubConnection owner = await _host.ConnectAsync(local.Id);
        LocalPlaybackUpdateDto update = Update(1) with { Track = Update(1).Track! with { DurationMs = 5_000 } };

        int result = await owner.InvokeAsync<int>(PUBLISH, update);

        result.Should().Be((int)LocalPublishResult.Invalid);
    }

    [Fact]
    public async Task SpotifyPlayerCommand_FromLocalSession_Fails()
    {
        UserSessionEntity local = await _host.Store.SeedLocalSessionAsync("Desktop");
        HubConnection owner = await _host.ConnectAsync(local.Id);

        int result = await owner.InvokeAsync<int>(nameof(PlaybackHub.SendPlayerCommand), "pause");

        result.Should().Be((int)PlayerCommandResult.Failed);
    }

    [Fact]
    public async Task PublisherDisconnect_AfterGrace_ShowsDisplaysPausedPlayback()
    {
        UserSessionEntity local = await _host.Store.SeedLocalSessionAsync("Desktop");
        CreatedDisplay tv = await _host.Store.CreateDisplayService().CreateAsync(local.Id, "TV");
        HubConnection owner = await _host.ConnectAsync(local.Id);
        HubConnection display = await _host.ConnectDisplayAsync(tv);
        Task<PlaybackStateDto> playing = NextStateWithTrackAsync(display);
        (await owner.InvokeAsync<int>(PUBLISH, Update(1))).Should().Be((int)LocalPublishResult.Accepted);
        await DisplayTestHost.WaitAsync(playing);

        Task<PlaybackStateDto> paused = NextAsync<PlaybackStateDto>(
            display,
            nameof(IPlaybackClient.ReceivePlaybackState),
            state => !state.IsPlaying);
        Task<DiagnosticsDto> offline = NextAsync<DiagnosticsDto>(
            display,
            nameof(IPlaybackClient.ReceiveDiagnostics),
            diagnostics => diagnostics.PollerStatus == PlaybackUpdateApplier.STATUS_PUBLISHER_OFFLINE);
        string ownerConnectionId = owner.ConnectionId!;
        await owner.StopAsync();
        await _host.WaitForServerDisconnectAsync(ownerConnectionId);

        _host.Time.Advance(TimeSpan.FromSeconds(PUBLISHER_GRACE_SECONDS));

        PlaybackStateDto frozen = await DisplayTestHost.WaitAsync(paused);
        frozen.CurrentTrack.Should().NotBeNull("the track and lyrics are kept while the publisher is away");
        frozen.ActiveUserId.Should().BeNull();
        await DisplayTestHost.WaitAsync(offline);
    }

    private static Task<PlaybackStateDto> NextStateWithTrackAsync(HubConnection connection) =>
        NextAsync<PlaybackStateDto>(connection, nameof(IPlaybackClient.ReceivePlaybackState), s => s.CurrentTrack is not null);

    private static Task<T> NextAsync<T>(HubConnection connection, string method, Func<T, bool>? predicate = null)
    {
        TaskCompletionSource<T> received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IDisposable subscription = connection.On<T>(method, payload =>
        {
            if (predicate is null || predicate(payload))
            {
                received.TrySetResult(payload);
            }
        });
        received.Task.ContinueWith(_ => subscription.Dispose(), TaskScheduler.Default);
        return received.Task;
    }

    private static LocalPlaybackUpdateDto Update(long sequence)
    {
        return new LocalPlaybackUpdateDto
        {
            Sequence = sequence,
            Player = new LocalPlayerDto { Id = "spotify", Name = "Spotify" },
            DeviceName = "Spotify",
            Track = new LocalTrackDto
            {
                Title = "Glimpse of Us",
                Artist = "Joji",
                Album = "SMITHEREENS",
                DurationMs = 233_000,
                SpotifyTrackId = "2mlNgAeIBnL78ZriXgrRHz"
            },
            IsPlaying = true,
            PositionMs = 98_774,
            Rate = 1.0,
            Capabilities = new PlaybackCapabilitiesDto { CanPlay = true, CanPause = true, CanSeek = true }
        };
    }
}
