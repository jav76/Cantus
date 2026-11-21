using System.Text;

namespace Cantus.Core.Search;

/// <summary>
/// A small Metaphone-style key ("Metaphone-lite"), not real Double Metaphone.
/// Rewrites kn, wr, ph, ck, silent gh, dg, ch, sh, th, soft and hard c, q, x
/// and z, then keeps the first sound and drops vowels, h, w and y after it,
/// collapsing repeated symbols.
/// </summary>
internal static class PhoneticKey
{
    private const char SH_SOUND = 'X';
    private const char TH_SOUND = '0';

    /// <summary>
    /// Returns the key, or null when the token is too short, has no letters,
    /// or its key is shorter than <see cref="LyricMatchConstants.MIN_PHONETIC_KEY_LENGTH"/>.
    /// </summary>
    public static string? Compute(string token)
    {
        if (token.Length < LyricMatchConstants.MIN_PHONETIC_TOKEN_LENGTH || !token.Any(char.IsLetter))
        {
            return null;
        }

        string sounds = Rewrite(token);
        if (sounds.Length == 0)
        {
            return null;
        }

        StringBuilder key = new();
        key.Append(sounds[0]);
        for (int i = 1; i < sounds.Length; i++)
        {
            char c = sounds[i];
            if (c is 'a' or 'e' or 'i' or 'o' or 'u' or 'h' or 'w' or 'y')
            {
                continue;
            }

            if (c != key[^1])
            {
                key.Append(c);
            }
        }

        return key.Length >= LyricMatchConstants.MIN_PHONETIC_KEY_LENGTH ? key.ToString() : null;
    }

    private static string Rewrite(string token)
    {
        StringBuilder sounds = new(token.Length + 1);
        int i = 0;
        while (i < token.Length)
        {
            char c = token[i];
            char next = i + 1 < token.Length ? token[i + 1] : '\0';

            switch (c)
            {
                case 'k' when next == 'n':
                case 'w' when next == 'r':
                    i++;
                    continue;
                case 'p' when next == 'h':
                    sounds.Append('f');
                    i += 2;
                    continue;
                case 'c' when next == 'k':
                    sounds.Append('k');
                    i += 2;
                    continue;
                case 'g' when next == 'h':
                    if (i == 0)
                    {
                        sounds.Append('g');
                    }

                    i += 2;
                    continue;
                case 'd' when next == 'g':
                    sounds.Append('j');
                    i += 2;
                    continue;
                case 'c' when next == 'h':
                case 's' when next == 'h':
                    sounds.Append(SH_SOUND);
                    i += 2;
                    continue;
                case 't' when next == 'h':
                    sounds.Append(TH_SOUND);
                    i += 2;
                    continue;
                case 'c':
                    sounds.Append(next is 'e' or 'i' or 'y' ? 's' : 'k');
                    break;
                case 'q':
                    sounds.Append('k');
                    break;
                case 'x':
                    sounds.Append("ks");
                    break;
                case 'z':
                    sounds.Append('s');
                    break;
                default:
                    sounds.Append(c);
                    break;
            }

            i++;
        }

        return sounds.ToString();
    }
}
