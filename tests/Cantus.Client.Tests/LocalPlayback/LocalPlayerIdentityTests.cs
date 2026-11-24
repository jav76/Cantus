using Cantus.Client.LocalPlayback;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.LocalPlayback;

public sealed class LocalPlayerIdentityTests
{
    [Theory]
    [InlineData(MprisHostMaps.SPOTIFY_BUS_NAME, "spotify", "spotify")]
    [InlineData("org.mpris.MediaPlayer2.spotify", "Spotify", "spotify")]
    [InlineData("org.mpris.MediaPlayer2.vlc.instance1234", "  org.videolan.VLC ", "org.videolan.vlc")]
    [InlineData(MprisHostMaps.BRAVE_BUS_NAME, null, "brave")]
    [InlineData(MprisHostMaps.CHROMIUM_BUS_NAME, null, "chromium")]
    [InlineData("org.mpris.MediaPlayer2.firefox.instance_1_42", null, "firefox.instance_1_42")]
    [InlineData("org.mpris.MediaPlayer2.foo.instance", null, "foo.instance")]
    [InlineData("org.mpris.MediaPlayer2.playerctld", "", "playerctld")]
    [InlineData("org.mpris.MediaPlayer2.kdeconnect.mpris_000001", "   ", "kdeconnect.mpris_000001")]
    public void ResolveAppId_PrefersTheDesktopEntryThenTheBusNameWithoutTheInstance(
        string busName,
        string? desktopEntry,
        string expected)
    {
        LocalPlayerIdentity.ResolveAppId(busName, desktopEntry).Should().Be(expected);
    }

    [Theory]
    [InlineData("Spotify", "spotify", "Spotify")]
    [InlineData(" Brave ", "brave", "Brave")]
    [InlineData(null, "brave", "brave")]
    [InlineData("", "chromium", "chromium")]
    public void ResolveAppName_UsesTheIdentityOrFallsBackToTheAppId(string? identity, string appId, string expected)
    {
        LocalPlayerIdentity.ResolveAppName(identity, appId).Should().Be(expected);
    }

    [Theory]
    [InlineData(MprisHostMaps.SPOTIFY_BUS_NAME, true)]
    [InlineData(MprisHostMaps.BRAVE_BUS_NAME, true)]
    [InlineData("org.mpris.MediaPlayer2.", false)]
    [InlineData("org.freedesktop.DBus", false)]
    [InlineData(":1.8641", false)]
    [InlineData(null, false)]
    public void IsMprisBusName_MatchesOnlyMprisPlayers(string? busName, bool expected)
    {
        LocalPlayerIdentity.IsMprisBusName(busName).Should().Be(expected);
    }
}
