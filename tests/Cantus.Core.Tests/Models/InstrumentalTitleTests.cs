using Cantus.Core.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Core.Tests.Models;

public class InstrumentalTitleTests
{
    [Theory]
    [InlineData("Song (Instrumental)")]
    [InlineData("Song [Instrumental Version]")]
    [InlineData("Song (Inst.)")]
    [InlineData("Song (Off Vocal)")]
    [InlineData("Song (off-vocal ver.)")]
    [InlineData("Song (Karaoke)")]
    [InlineData("Song - Instrumental")]
    [InlineData("Song - Remastered 2011 - Instrumental")]
    [InlineData("Song – Karaoke Version")]
    [InlineData("Song (Backing Track)")]
    [InlineData("Song (No Vocals)")]
    [InlineData("Song (feat. Singer) [Instrumental]")]
    public void IsMarked_WhenAQualifierSaysThereAreNoVocals_ReturnsTrue(string title)
    {
        InstrumentalTitle.IsMarked(title).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Instrumental")]
    [InlineData("Gwen, the Hallowed Seamstress")]
    [InlineData("Song (Live)")]
    [InlineData("Song - Remastered 2011")]
    [InlineData("Song (Installation Mix)")]
    [InlineData("Karaoke Night")]
    [InlineData("Instrumentally Yours")]
    [InlineData("Self-Instrumental")]
    public void IsMarked_WhenNoQualifierSaysThereAreNoVocals_ReturnsFalse(string? title)
    {
        InstrumentalTitle.IsMarked(title).Should().BeFalse();
    }
}
