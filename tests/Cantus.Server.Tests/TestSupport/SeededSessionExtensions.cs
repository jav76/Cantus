using Cantus.Infrastructure.Persistence.Entities;

namespace Cantus.Server.Tests.TestSupport;

public static class SeededSessionExtensions
{
    /// <summary>The Spotify user id of a seeded Spotify Session, which always has one.</summary>
    public static string RequiredSpotifyUserId(this UserSessionEntity session) =>
        session.SpotifyUserId ?? throw new InvalidOperationException("The seeded Session is not a Spotify Session.");
}
