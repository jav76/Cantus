using System;
using System.Threading.Tasks;
using Cantus.Client.Services;

namespace Cantus.Client.LocalPlayback;

/// <summary>Where <see cref="LocalPlaybackPublisher"/> sends updates: the hub connection in the app.</summary>
public interface ILocalPlaybackSink
{
    bool IsConnected { get; }

    /// <summary>Server clock minus client clock in ms, or null before the first clock sample.</summary>
    long? ServerClockOffsetMs { get; }

    /// <summary>Raised after every new connection, including automatic reconnects.</summary>
    event Action? Connected;

    /// <summary>Returns a <see cref="LocalPublishResult"/> value.</summary>
    Task<int> PublishAsync(LocalPlaybackUpdatePayload payload);
}
