namespace Cantus.Client.LocalPlayback;

/// <summary>
/// The hub's <c>PublishLocalPlayback</c> result codes, plus two that never travel on the wire:
/// <see cref="NotConnected"/> when no call reached the server, and <see cref="ServerError"/> when the
/// server failed the call (or does not have the method) instead of answering with a code.
/// </summary>
public enum LocalPublishResult
{
    ServerError = -2,
    NotConnected = -1,
    Accepted = 0,
    NotLocalSession = 1,
    NotPublisher = 2,
    RateLimited = 3,
    Invalid = 4,
}
