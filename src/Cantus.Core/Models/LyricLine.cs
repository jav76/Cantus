namespace Cantus.Core.Models;

public sealed record LyricSyllable(TimeSpan Timestamp, TimeSpan Duration, string Text);

public sealed record LyricLine(TimeSpan Timestamp, string Text, IReadOnlyList<LyricSyllable>? Syllables = null);
