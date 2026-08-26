namespace Cantus.Core.Models;

public sealed record LyricSyllable(TimeSpan Timestamp, TimeSpan Duration, string Text);

public sealed record LyricWordTimestamp(string Word, TimeSpan Timestamp, int WordIndex);

public sealed record LyricLine(TimeSpan Timestamp, string Text, IReadOnlyList<LyricSyllable>? Syllables = null)
{
    public IReadOnlyList<LyricWordTimestamp> GetWordTimestamps(TimeSpan? lineDuration = null)
    {
        if (Syllables is { Count: > 0 })
        {
            var resultFromSyllables = new List<LyricWordTimestamp>(Syllables.Count);
            for (int i = 0; i < Syllables.Count; i++)
            {
                var s = Syllables[i];
                resultFromSyllables.Add(new LyricWordTimestamp(s.Text, s.Timestamp, i));
            }
            return resultFromSyllables;
        }

        if (string.IsNullOrWhiteSpace(Text))
        {
            return [];
        }

        var words = Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return [];
        }

        if (words.Length == 1)
        {
            return [new LyricWordTimestamp(words[0], Timestamp, 0)];
        }

        var duration = lineDuration.GetValueOrDefault(TimeSpan.FromSeconds(Math.Clamp(words.Length * 0.45, 1.5, 6.0)));
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromSeconds(3.0);
        }

        int totalChars = Text.Length;
        var result = new List<LyricWordTimestamp>(words.Length);
        int currentPos = 0;

        for (int i = 0; i < words.Length; i++)
        {
            int wordPos = Text.IndexOf(words[i], currentPos, StringComparison.Ordinal);
            if (wordPos < 0) wordPos = currentPos;
            currentPos = wordPos + words[i].Length;

            double ratio = totalChars > 0 ? (double)wordPos / totalChars : 0.0;
            var wordTimestamp = Timestamp + TimeSpan.FromMilliseconds(duration.TotalMilliseconds * ratio);
            result.Add(new LyricWordTimestamp(words[i], wordTimestamp, i));
        }

        return result;
    }
}

