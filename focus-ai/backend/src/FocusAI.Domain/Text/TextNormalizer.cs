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
    /// <remarks>
    /// Entries are matched against already-normalised tokens, so they have to be
    /// written the way <see cref="Normalize"/> leaves them — "icin", not "için".
    /// The diacritic form could never fire: by the time the list is consulted the
    /// cedilla is gone.
    /// </remarks>
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "and", "or", "for", "to", "of", "in", "on", "with", "is",
        "are", "be", "as", "at", "by", "from", "that", "this", "it", "its",
        "ve", "ile", "bir", "bu", "de", "da", "icin", "olarak"
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
    /// <remarks>
    /// The <c>ToLowerInvariant</c> fallback is load-bearing, not defensive tidying.
    /// <c>ToLowerInvariant</c> leaves 'İ' (U+0130) untouched — the same trap
    /// CommitmentLexicon.Fold and VisualSubject.FoldCase spell out by hand — so it
    /// arrives here having survived the lowercase pass, and FormD has by then split
    /// it into 'I' + combining dot. The dot is dropped as a diacritic and a bare
    /// capital 'I' is left standing in a string this method promises is lowercase.
    ///
    /// That is how "VIGOR: Dil Modelleri İçin …" was slugged
    /// "…-modelleri-Icin-varyans-…", and, because the detail lookup lowercases the
    /// slug it is given before matching, how the story answered 404 to its own URL
    /// — permanently, and only ever for Turkish headlines carrying a dotted capital
    /// I.
    /// </remarks>
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
        _ => char.ToLowerInvariant(ch)
    };
}
