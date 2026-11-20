namespace Cantus.Client.Models;

/// <summary>
/// One sung syllable of a lyric line: absolute start and duration in milliseconds, and its text
/// exactly as sent (a word's trailing space stays on it).
/// </summary>
public sealed record SyllableTiming(long TimestampMs, long DurationMs, string Text);
