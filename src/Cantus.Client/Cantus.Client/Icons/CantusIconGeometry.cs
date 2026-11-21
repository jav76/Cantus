using System.Globalization;
using System.Text;

namespace Cantus.Client.Icons;

/// <summary>
/// Turns an icon's 256-unit path data into path markup drawn in a size x size box.
/// The geometry itself is scaled (not a render transform), so PathIcon draws it at its native size on every renderer.
/// </summary>
public static class CantusIconGeometry
{
    public const double SOURCE_SIZE = 256.0;

    private static readonly Dictionary<(CantusIconKind Kind, double Size), string> Cache = new();

    /// <summary>
    /// Path markup with an explicit nonzero fill rule ("F1"): Phosphor's SVGs rely on SVG's default nonzero fill,
    /// while XAML path markup without a prefix fills even-odd.
    /// </summary>
    public static string GetPathMarkup(CantusIconKind kind, double size)
    {
        if (!(size > 0) || double.IsInfinity(size))
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "Icon size must be a positive number.");
        }

        lock (Cache)
        {
            if (!Cache.TryGetValue((kind, size), out string? markup))
            {
                markup = "F1 " + Scale(CantusIconPaths.Get(kind), size / SOURCE_SIZE);
                Cache[(kind, size)] = markup;
            }
            return markup;
        }
    }

    /// <summary>
    /// Multiplies every coordinate in space-separated path data by <paramref name="factor"/>.
    /// Arc rotation and the two arc flags are angles and booleans, so they are kept as they are.
    /// </summary>
    internal static string Scale(string pathData, double factor)
    {
        var builder = new StringBuilder(pathData.Length);
        char command = ' ';
        int argumentIndex = 0;
        foreach (string token in pathData.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            if (char.IsLetter(token[0]))
            {
                command = char.ToUpperInvariant(token[0]);
                argumentIndex = 0;
                builder.Append(token);
                continue;
            }

            bool keep = command == 'A' && argumentIndex % 7 is 2 or 3 or 4;
            if (keep)
            {
                builder.Append(token);
            }
            else
            {
                double value = Math.Round(double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture) * factor, 3);
                builder.Append(value == 0 ? "0" : value.ToString("0.###", CultureInfo.InvariantCulture));
            }
            argumentIndex++;
        }
        return builder.ToString();
    }
}
