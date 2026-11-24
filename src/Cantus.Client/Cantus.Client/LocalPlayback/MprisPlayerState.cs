using System;
using System.Collections.Generic;

namespace Cantus.Client.LocalPlayback;

/// <summary>
/// What one MPRIS player has reported so far, kept as plain values so it can be tested without a bus.
/// MPRIS never signals position changes, so the position is a sample plus the time it was taken,
/// extrapolated with the rate; it is resampled whenever status, track or rate change.
/// </summary>
public sealed class MprisPlayerState
{
    public const string PROPERTY_IDENTITY = "Identity";
    public const string PROPERTY_DESKTOP_ENTRY = "DesktopEntry";
    public const string PROPERTY_PLAYBACK_STATUS = "PlaybackStatus";
    public const string PROPERTY_METADATA = "Metadata";
    public const string PROPERTY_RATE = "Rate";
    public const string PROPERTY_POSITION = "Position";
    public const string PROPERTY_CAN_CONTROL = "CanControl";
    public const string PROPERTY_CAN_PLAY = "CanPlay";
    public const string PROPERTY_CAN_PAUSE = "CanPause";
    public const string PROPERTY_CAN_GO_NEXT = "CanGoNext";
    public const string PROPERTY_CAN_GO_PREVIOUS = "CanGoPrevious";
    public const string PROPERTY_CAN_SEEK = "CanSeek";

    private const double RATE_CHANGE_TOLERANCE = 1e-6;

    private string? _identity;
    private string? _desktopEntry;
    private bool _canControl = true;
    private bool _canPlay;
    private bool _canPause;
    private bool _canGoNext;
    private bool _canGoPrevious;
    private bool _canSeek;

    public MprisPlayerState(string busName, string owner)
    {
        BusName = busName;
        Owner = owner;
    }

    public string BusName { get; }

    /// <summary>The unique connection name signals arrive from.</summary>
    public string Owner { get; }

    public LocalPlayerStatus Status { get; private set; } = LocalPlayerStatus.Stopped;

    public LocalTrack? Track { get; private set; }

    public string? TrackObjectPath { get; private set; }

    public TimeSpan Position { get; private set; }

    public TimeSpan PositionCapturedAt { get; private set; }

    public double Rate { get; private set; } = 1.0;

    public TimeSpan? LastStartedPlayingAt { get; private set; }

    public int SeekGeneration { get; private set; }

    /// <summary>
    /// Bumped whenever the position base changes for a reason other than <see cref="SetPosition"/>, so a
    /// position read that was started earlier can tell its answer is stale.
    /// </summary>
    public int TimelineGeneration { get; private set; }

    public string AppId => LocalPlayerIdentity.ResolveAppId(BusName, _desktopEntry);

    public string AppName => LocalPlayerIdentity.ResolveAppName(_identity, AppId);

    public LocalPlayerCapabilities Capabilities => _canControl
        ? new LocalPlayerCapabilities(_canPlay, _canPause, _canGoNext, _canGoPrevious, _canSeek)
        : LocalPlayerCapabilities.None;

    public void ApplyRootProperties(IReadOnlyDictionary<string, object?> properties)
    {
        if (properties.TryGetValue(PROPERTY_IDENTITY, out object? identity) && identity is string name)
        {
            _identity = name;
        }

        if (properties.TryGetValue(PROPERTY_DESKTOP_ENTRY, out object? entry) && entry is string desktopEntry)
        {
            _desktopEntry = desktopEntry;
        }
    }

    /// <summary>
    /// Applies <c>org.mpris.MediaPlayer2.Player</c> properties from <c>GetAll</c> or <c>PropertiesChanged</c>.
    /// Returns true when the position should be read again because status, track or rate changed (or
    /// the track may have changed) and the properties did not carry a fresh position.
    /// </summary>
    public bool ApplyPlayerProperties(IReadOnlyDictionary<string, object?> properties, TimeSpan now)
    {
        bool timelineChanged = false;
        bool positionSuspect = false;

        if (properties.TryGetValue(PROPERTY_PLAYBACK_STATUS, out object? statusValue)
            && statusValue is string statusText)
        {
            LocalPlayerStatus status = ParseStatus(statusText);
            if (status != Status)
            {
                Freeze(now);
                if (status == LocalPlayerStatus.Playing)
                {
                    LastStartedPlayingAt = now;
                }

                Status = status;
                timelineChanged = true;
            }
        }

        if (properties.TryGetValue(PROPERTY_METADATA, out object? metadataValue))
        {
            IReadOnlyDictionary<string, object?>? metadata = metadataValue as IReadOnlyDictionary<string, object?>;
            LocalTrack? track = MprisMetadataParser.Parse(metadata);
            string? trackPath = MprisMetadataParser.ReadTrackObjectPath(metadata);
            if (IsDifferentTrack(track, trackPath))
            {
                Track = track;
                TrackObjectPath = trackPath;
                Position = TimeSpan.Zero;
                PositionCapturedAt = now;
                timelineChanged = true;
            }
            else if (track != Track)
            {
                // Art, album or a late duration for the same track: the timeline carries on. Without a
                // trackid this may also be the next track under the same title and artist, so the
                // position is read again to settle which.
                Freeze(now);
                Track = track;
                positionSuspect = trackPath is null;
            }
        }

        if (properties.TryGetValue(PROPERTY_RATE, out object? rateValue)
            && rateValue is double rate
            && double.IsFinite(rate)
            && Math.Abs(rate - Rate) > RATE_CHANGE_TOLERANCE)
        {
            Freeze(now);
            Rate = rate;
            timelineChanged = true;
        }

        ApplyFlag(properties, PROPERTY_CAN_CONTROL, ref _canControl);
        ApplyFlag(properties, PROPERTY_CAN_PLAY, ref _canPlay);
        ApplyFlag(properties, PROPERTY_CAN_PAUSE, ref _canPause);
        ApplyFlag(properties, PROPERTY_CAN_GO_NEXT, ref _canGoNext);
        ApplyFlag(properties, PROPERTY_CAN_GO_PREVIOUS, ref _canGoPrevious);
        ApplyFlag(properties, PROPERTY_CAN_SEEK, ref _canSeek);

        if (properties.TryGetValue(PROPERTY_POSITION, out object? positionValue)
            && MprisMetadataParser.ToInt64(positionValue) is long microseconds)
        {
            SetPosition(microseconds, now);
            TimelineGeneration++;
            return false;
        }

        if (timelineChanged)
        {
            TimelineGeneration++;
        }

        return timelineChanged || positionSuspect;
    }

    public void SetPosition(long microseconds, TimeSpan now)
    {
        Position = TimeSpan.FromTicks(Math.Max(0, microseconds) * TimeSpan.TicksPerMicrosecond);
        PositionCapturedAt = now;
    }

    /// <summary>The <c>Seeked</c> signal: the player jumped to a new position.</summary>
    public void ApplySeeked(long microseconds, TimeSpan now)
    {
        SetPosition(microseconds, now);
        SeekGeneration++;
        TimelineGeneration++;
    }

    public LocalPlayerSnapshot ToSnapshot() => new(
        BusName,
        AppId,
        AppName,
        Status,
        Track,
        Position,
        PositionCapturedAt,
        Rate,
        Capabilities,
        LastStartedPlayingAt,
        SeekGeneration);

    public static LocalPlayerStatus ParseStatus(string? status) => status switch
    {
        "Playing" => LocalPlayerStatus.Playing,
        "Paused" => LocalPlayerStatus.Paused,
        _ => LocalPlayerStatus.Stopped,
    };

    private bool IsDifferentTrack(LocalTrack? track, string? trackPath) =>
        trackPath != TrackObjectPath
        || (track is null) != (Track is null)
        || !string.Equals(track?.Title, Track?.Title, StringComparison.Ordinal)
        || !string.Equals(track?.Artist, Track?.Artist, StringComparison.Ordinal);

    private void Freeze(TimeSpan now)
    {
        Position = LocalPlayerSnapshot.Project(Status, Position, PositionCapturedAt, Rate, Track?.Duration ?? TimeSpan.Zero, now);
        PositionCapturedAt = now;
    }

    private static void ApplyFlag(IReadOnlyDictionary<string, object?> properties, string name, ref bool field)
    {
        if (properties.TryGetValue(name, out object? value) && value is bool flag)
        {
            field = flag;
        }
    }
}
