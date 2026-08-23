namespace Cantus.Core.Models;

public sealed class SyncedLyrics
{
    public required string TrackId { get; init; }
    public required string Title { get; init; }
    public required string Artist { get; init; }
    public string? Album { get; init; }
    public IReadOnlyList<LyricLine> Lines { get; init; } = [];
    public bool IsSynced { get; init; }
    public bool IsInstrumental { get; init; }
    public string? PlainLyrics { get; init; }

    public int GetActiveLineIndex(TimeSpan position)
    {
        if (Lines.Count == 0 || position < Lines[0].Timestamp)
        {
            return -1;
        }

        int low = 0;
        int high = Lines.Count - 1;
        int result = 0;

        while (low <= high)
        {
            int mid = low + ((high - low) >> 1);
            if (Lines[mid].Timestamp <= position)
            {
                result = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return result;
    }

    public LyricLine? GetActiveLine(TimeSpan position)
    {
        int index = GetActiveLineIndex(position);
        return index >= 0 && index < Lines.Count ? Lines[index] : null;
    }
}
