using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class PairingCodeLayoutTests
{
    [Fact]
    public void Small_StacksCodeUnderQrAcrossBothColumns()
    {
        PairingCodeLayout.IsStacked(LayoutBreakpoint.Small).Should().BeTrue();
        PairingCodeLayout.GetCodeRow(LayoutBreakpoint.Small).Should().Be(1);
        PairingCodeLayout.GetCodeColumn(LayoutBreakpoint.Small).Should().Be(0);
        PairingCodeLayout.GetColumnSpan(LayoutBreakpoint.Small).Should().Be(2);
        PairingCodeLayout.GetRowSpacing(LayoutBreakpoint.Small).Should().Be(PairingCodeLayout.ITEM_SPACING);
        PairingCodeLayout.GetColumnSpacing(LayoutBreakpoint.Small).Should().Be(0);
    }

    // Column 1 is the star column: the only placement that bounds the code's width beside the QR.
    [Theory]
    [InlineData(LayoutBreakpoint.Medium)]
    [InlineData(LayoutBreakpoint.Large)]
    [InlineData(LayoutBreakpoint.FullscreenTv)]
    public void WiderLayouts_PutCodeInStarColumnBesideQr(LayoutBreakpoint breakpoint)
    {
        PairingCodeLayout.IsStacked(breakpoint).Should().BeFalse();
        PairingCodeLayout.GetCodeRow(breakpoint).Should().Be(0);
        PairingCodeLayout.GetCodeColumn(breakpoint).Should().Be(1);
        PairingCodeLayout.GetColumnSpan(breakpoint).Should().Be(1);
        PairingCodeLayout.GetRowSpacing(breakpoint).Should().Be(0);
        PairingCodeLayout.GetColumnSpacing(breakpoint).Should().Be(PairingCodeLayout.ITEM_SPACING);
    }
}
