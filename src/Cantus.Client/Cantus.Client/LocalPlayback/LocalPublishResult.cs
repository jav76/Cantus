namespace Cantus.Client.LocalPlayback;

/// <summary>
/// The hub's <c>PublishLocalPlayback</c> result codes, plus <see cref="NotConnected"/>, which never
/// travels on the wire: the client returns it when no call reached the server.
/// </summary>
public enum LocalPublishResult
{
    NotConnected = -1,
    Accepted = 0,
    NotLocalSession = 1,
    NotPublisher = 2,
    RateLimited = 3,
    Invalid = 4,
}
