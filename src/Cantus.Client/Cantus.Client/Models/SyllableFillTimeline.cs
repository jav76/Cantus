using System;
using System.Collections.Generic;

namespace Cantus.Client.Models;

/// <summary>
/// Maps a playback position to how far along a line's text the karaoke fill has reached, from real
/// syllable timing. Each syllable's visible characters fill over its own duration. Whitespace and
/// the time between syllables are not sung, so the fill holds still through them and jumps a
/// space only when the next syllable starts. Width is approximated by character count, which is
/// exact for CJK lines (one syllable per character) and close for proportional Latin text.
/// </summary>
public sealed class SyllableFillTimeline
{
    private readonly IReadOnlyList<Segment> _segments;
    private readonly int _totalChars;

    private SyllableFillTimeline(IReadOnlyList<Segment> segments, int totalChars, long endMs)
    {
        _segments = segments;
        _totalChars = totalChars;
        EndMs = endMs;
    }

    /// <summary>When the last syllable ends and the fill is complete.</summary>
    public long EndMs { get; }

    /// <summary>
    /// Builds the timeline, or returns null when no syllable has visible text (the line then uses
    /// the elapsed-time fill).
    /// </summary>
    public static SyllableFillTimeline? Create(IReadOnlyList<SyllableTiming>? syllables)
    {
        if (syllables is null || syllables.Count == 0)
        {
            return null;
        }

        List<Segment> segments = new(syllables.Count);
        int offset = 0;
        int firstVisibleChar = -1;
        int lastVisibleChar = 0;
        long endMs = long.MinValue;

        for (int i = 0; i < syllables.Count; i++)
        {
            SyllableTiming? syllable = syllables[i];
            if (syllable is null)
            {
                continue;
            }

            string text = syllable.Text ?? string.Empty;
            int leading = CountLeadingWhitespace(text);
            int visible = text.Trim().Length;
            int charStart = offset + leading;
            offset += text.Length;

            if (visible == 0)
            {
                continue;
            }

            if (firstVisibleChar < 0)
            {
                firstVisibleChar = charStart;
            }

            lastVisibleChar = charStart + visible;
            long startMs = syllable.TimestampMs;
            long syllableEndMs = startMs + Math.Max(syllable.DurationMs, 0);
            endMs = Math.Max(endMs, syllableEndMs);
            segments.Add(new Segment(startMs, syllableEndMs, charStart, charStart + visible));
        }

        if (segments.Count == 0)
        {
            return null;
        }

        int totalChars = lastVisibleChar - firstVisibleChar;
        for (int i = 0; i < segments.Count; i++)
        {
            Segment segment = segments[i];
            segments[i] = segment with
            {
                CharStart = segment.CharStart - firstVisibleChar,
                CharEnd = segment.CharEnd - firstVisibleChar,
            };
        }

        return new SyllableFillTimeline(segments, totalChars, endMs);
    }

    /// <summary>Filled fraction of the line, 0..1, at the given playback position.</summary>
    public double ComputeFraction(long positionMs)
    {
        double reachedChars = 0.0;
        for (int i = 0; i < _segments.Count; i++)
        {
            Segment segment = _segments[i];
            if (positionMs < segment.StartMs)
            {
                continue;
            }

            double segmentChars = positionMs >= segment.EndMs
                ? segment.CharEnd
                : segment.CharStart + ((segment.CharEnd - segment.CharStart)
                    * (positionMs - segment.StartMs) / (double)(segment.EndMs - segment.StartMs));
            reachedChars = Math.Max(reachedChars, segmentChars);
        }

        return Math.Clamp(reachedChars / _totalChars, 0.0, 1.0);
    }

    private static int CountLeadingWhitespace(string text)
    {
        int count = 0;
        while (count < text.Length && char.IsWhiteSpace(text[count]))
        {
            count++;
        }

        return count;
    }

    private readonly record struct Segment(long StartMs, long EndMs, int CharStart, int CharEnd);
}
