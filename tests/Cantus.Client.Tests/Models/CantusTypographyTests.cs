using Cantus.Client.Models;
using FluentAssertions;
using Xunit;

namespace Cantus.Client.Tests.Models;

public sealed class CantusTypographyTests
{
    [Fact]
    public void GetLyricLineSource_ActiveLineUsesSemiBoldDisplayFont()
    {
        CantusTypography.GetLyricLineSource(isActive: true).Should().Be(CantusTypography.DISPLAY_SEMIBOLD_SOURCE);
        CantusTypography.GetLyricLineSource(isActive: false).Should().Be(CantusTypography.DISPLAY_REGULAR_SOURCE);
    }

    [Theory]
    [InlineData(CantusTypography.TEXT_REGULAR_SOURCE, "Figtree-Regular.ttf")]
    [InlineData(CantusTypography.DISPLAY_REGULAR_SOURCE, "Fraunces-Regular.ttf")]
    [InlineData(CantusTypography.DISPLAY_SEMIBOLD_SOURCE, "Fraunces-SemiBold.ttf")]
    public void Sources_PointAtBundledFontFiles(string source, string fileName)
    {
        source.Should().StartWith($"ms-appx:///Assets/Fonts/{fileName}#");
        string path = Path.Combine(FindRepositoryRoot(), "src", "Cantus.Client", "Cantus.Client", "Assets", "Fonts", fileName);
        File.Exists(path).Should().BeTrue($"{fileName} is bundled with the client");
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Cantus.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Cantus.slnx not found above the test output");
    }
}
