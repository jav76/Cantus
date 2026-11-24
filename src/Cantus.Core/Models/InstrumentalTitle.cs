using System.Text.RegularExpressions;

namespace Cantus.Core.Models;

/// <summary>
/// Recognises a track whose title says it is a version without vocals, such as
/// <c>Song (Instrumental)</c>, <c>Song [Off Vocal]</c> or <c>Song - Karaoke Version</c>.
/// Only a bracketed qualifier or a part after a spaced dash counts, so a song
/// that is simply named "Instrumental" is not matched.
/// </summary>
public static partial class InstrumentalTitle
{
    [GeneratedRegex(
        @"\b(?:instrumental|inst|karaoke|off[\s-]?vocals?|backing\s+track|no\s+vocals?|without\s+vocals?)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoVocalsQualifier();

    [GeneratedRegex(@"[\(\[]([^\)\]]*)[\)\]]", RegexOptions.CultureInvariant)]
    private static partial Regex BracketSegment();

    [GeneratedRegex(@"\s+[-–—]\s+", RegexOptions.CultureInvariant)]
    private static partial Regex DashSeparator();

    public static bool IsMarked(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        foreach (Match bracket in BracketSegment().Matches(title))
        {
            if (NoVocalsQualifier().IsMatch(bracket.Groups[1].Value))
            {
                return true;
            }
        }

        string[] dashParts = DashSeparator().Split(title);
        return dashParts.Skip(1).Any(part => NoVocalsQualifier().IsMatch(part));
    }
}
