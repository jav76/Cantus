using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class HeaderStatusTests
{
    [Theory]
    [InlineData("Active (Playing)")]
    [InlineData("Active (Background)")]
    [InlineData("Paused")]
    [InlineData("Idle")]
    public void GetAlertText_ConnectedWithNormalPoller_ReturnsNull(string pollerStatus)
    {
        HeaderStatus.GetAlertText("Connected", pollerStatus).Should().BeNull();
    }

    [Theory]
    [InlineData("Connecting...")]
    [InlineData("Connecting")]
    [InlineData("Reconnecting")]
    [InlineData("Disconnected")]
    public void GetAlertText_ConnectionNotConnected_ShowsConnectionStatus(string connectionStatus)
    {
        HeaderStatus.GetAlertText(connectionStatus, "Rate Limited (30s)").Should().Be(connectionStatus);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetAlertText_MissingConnectionStatus_ShowsDisconnected(string? connectionStatus)
    {
        HeaderStatus.GetAlertText(connectionStatus, "Idle").Should().Be("Disconnected");
    }

    [Fact]
    public void GetAlertText_RateLimitedPoller_ShowsPollerStatus()
    {
        HeaderStatus.GetAlertText("Connected", "Rate Limited (1m 5s)").Should().Be("Rate Limited (1m 5s)");
    }

    [Theory]
    [InlineData("Rate Limited (5s)", true)]
    [InlineData("rate limited", true)]
    [InlineData("Active (Playing)", false)]
    [InlineData(null, false)]
    public void IsPollerAbnormal_MatchesRateLimitedPrefix(string? pollerStatus, bool expected)
    {
        HeaderStatus.IsPollerAbnormal(pollerStatus).Should().Be(expected);
    }

    [Fact]
    public void GetTooltip_ListsConnectionRttAndPoller()
    {
        HeaderStatus.GetTooltip("Connected", 42, "Active (Playing)")
            .Should().Be("Connection: Connected\nRTT: 42 ms\nPoller: Active (Playing)");
    }

    [Theory]
    [InlineData("Connected", StatusTone.Healthy)]
    [InlineData("Reconnecting", StatusTone.Warning)]
    [InlineData("Connecting...", StatusTone.Error)]
    [InlineData("Disconnected", StatusTone.Error)]
    [InlineData(null, StatusTone.Error)]
    public void GetStatusTone_FollowsConnectionStatus(string? connectionStatus, StatusTone expected)
    {
        HeaderStatus.GetStatusTone(connectionStatus).Should().Be(expected);
    }

    [Theory]
    [InlineData("Connected", StatusTone.Warning)]
    [InlineData("Reconnecting", StatusTone.Warning)]
    [InlineData("Disconnected", StatusTone.Error)]
    [InlineData(null, StatusTone.Error)]
    public void GetAlertTone_RateLimitIsWarningAndConnectionProblemKeepsStatusTone(string? connectionStatus, StatusTone expected)
    {
        HeaderStatus.GetAlertTone(connectionStatus).Should().Be(expected);
    }
}
