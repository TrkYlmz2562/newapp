using System.Globalization;
using System.Text;

namespace FocusAI.Domain.Text;

/// <summary>
/// Deterministic text normalisation shared by hashing, de-duplication and
/// tagging. Everything here must stay side-effect free and culture-independent,
/// because hashes computed on one machine are compared on another.
/// </summary>
public static class TextNormalizer
{
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "for", "to", "of", "in", "on", "with", "is",
        "are", "be", "as", "at", "by", "from", "that", "this", "it", "its",
        "ve", "ile", "bir", "bu", "de", "da", "için", "olarak"
    };

    /// <summary>
    /// Lowercases, strips diacritics, collapses everything that is not a letter
    /// or digit into single spaces. "GPT-6 Yayınlandı!" → "gpt 6 yayinlandi".
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return string.Empty;
        }

        var lowered = input.ToLowerInvariant();
        var decomposed = lowered.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = true;

        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(MapSpecialLetter(ch));
                lastWasSpace = false;
            }
            else if (!lastWasSpace)
            {
                builder.Append(' ');
                lastWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>Normalises, then drops stop words and one-character tokens.</summary>
    public static string[] Tokenize(string? input)
    {
        var normalized = Normalize(input);
        if (normalized.Length == 0)
        {
            return [];
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 1 && !NoiseTokens.Contains(t))
            .ToArray();
    }

    /// <summary>
    /// Word-level shingles. Overlapping pairs make SimHash far more sensitive to
    /// reordering than bare unigrams would be.
    /// </summary>
    public static IEnumerable<string> Shingles(string? input, int size = 2)
    {
        var tokens = Tokenize(input);
        if (tokens.Length == 0)
        {
            yield break;
        }

        if (tokens.Length < size)
        {
            yield return string.Join(' ', tokens);
            yield break;
        }

        for (var i = 0; i <= tokens.Length - size; i++)
        {
            yield return string.Join(' ', tokens.AsSpan(i, size).ToArray());
        }
    }

    /// <summary>
    /// Latin-1/Turkish letters that FormD does not decompose. Mapping 'ı' and
    /// 'i' to the same character is intentional: it makes Turkish titles that
    /// differ only in dotted-i collide, which is what de-duplication wants.
    /// </summary>
    private static char MapSpecialLetter(char ch) => ch switch
    {
        'ı' => 'i',
        'ğ' => 'g',
        'ş' => 's',
        'ø' => 'o',
        'æ' => 'a',
        'ß' => 's',
        'đ' => 'd',
        'ł' => 'l',
        _ => ch
    };
}
