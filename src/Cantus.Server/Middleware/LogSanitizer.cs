using System;

namespace Cantus.Server.Middleware;

/// <summary>Makes request-supplied text safe to write to a line-based log.</summary>
public static class LogSanitizer
{
    /// <summary>
    /// Replaces line breaks, other control characters and the Unicode line and paragraph
    /// separators with a space, so the text cannot start a forged log line.
    /// </summary>
    public static string SingleLine(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // The Replace calls come first because static analysis (CodeQL cs/log-forging) only
        // recognises String.Replace as a sanitizer, not the character loop below.
        string withoutLineBreaks = value
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal);

        return string.Create(withoutLineBreaks.Length, withoutLineBreaks, static (span, source) =>
        {
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                span[i] = char.IsControl(c) || c is '\u2028' or '\u2029' ? ' ' : c;
            }
        });
    }
}
