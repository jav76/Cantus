using Xunit;

namespace Cantus.Infrastructure.Tests.Spotify;

/// <summary>
/// <see cref="Cantus.Infrastructure.Spotify.SpotifyAuthService"/> caches Sessions in a static
/// dictionary, so test classes that create one must not run in parallel.
/// </summary>
[CollectionDefinition(NAME)]
public sealed class SpotifyAuthServiceCacheCollection
{
    public const string NAME = "SpotifyAuthService session cache";
}
