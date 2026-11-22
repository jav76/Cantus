using System.Buffers.Binary;
using System.Text;
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

    // The Windows head matches the "#name" fragment against the DirectWrite WWS family, which keeps any
    // typographic subfamily word that is not a weight term (the old "Fraunces 72pt" instances did not match).
    [Theory]
    [InlineData(CantusTypography.TEXT_REGULAR_SOURCE, "Figtree-Regular.ttf")]
    [InlineData(CantusTypography.DISPLAY_REGULAR_SOURCE, "Fraunces-Regular.ttf")]
    [InlineData(CantusTypography.DISPLAY_SEMIBOLD_SOURCE, "Fraunces-SemiBold.ttf")]
    public void Sources_FragmentMatchesFontFamilyName(string source, string fileName)
    {
        string family = source[(source.IndexOf('#') + 1)..];
        string path = Path.Combine(FindRepositoryRoot(), "src", "Cantus.Client", "Cantus.Client", "Assets", "Fonts", fileName);
        IReadOnlyDictionary<int, string> names = ReadWindowsNames(File.ReadAllBytes(path));

        string typographicFamily = names.GetValueOrDefault(16, names[1]);
        string typographicStyle = names.GetValueOrDefault(17, names[2]);

        typographicFamily.Should().Be(family);
        typographicStyle.Should().BeOneOf("Regular", "Medium", "SemiBold");
        names[1].Should().Be(typographicStyle == "Regular" ? family : $"{family} {typographicStyle}");
    }

    private static IReadOnlyDictionary<int, string> ReadWindowsNames(byte[] font)
    {
        int ReadUInt16(int offset) => BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(offset));
        int ReadUInt32(int offset) => (int)BinaryPrimitives.ReadUInt32BigEndian(font.AsSpan(offset));

        int tableCount = ReadUInt16(4);
        int nameTable = Enumerable.Range(0, tableCount)
            .Select(index => 12 + (index * 16))
            .Where(record => Encoding.ASCII.GetString(font, record, 4) == "name")
            .Select(record => ReadUInt32(record + 8))
            .Single();

        int recordCount = ReadUInt16(nameTable + 2);
        int strings = nameTable + ReadUInt16(nameTable + 4);
        Dictionary<int, string> names = [];
        for (int index = 0; index < recordCount; index++)
        {
            int record = nameTable + 6 + (index * 12);
            if (ReadUInt16(record) == 3 && ReadUInt16(record + 4) == 0x409)
            {
                names[ReadUInt16(record + 6)] = Encoding.BigEndianUnicode.GetString(font, strings + ReadUInt16(record + 10), ReadUInt16(record + 8));
            }
        }

        return names;
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
