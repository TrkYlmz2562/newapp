using System.Text;
using System.Text.RegularExpressions;

namespace FocusAI.Domain.Text;

/// <summary>
/// Picks the one term a headline is actually *about* — "M5", "OpenSSH", "20M₺",
/// "CVE-2026-1234" — so a card can show the subject instead of generic art.
/// </summary>
/// <remarks>
/// Everything here is extraction, never invention: <see cref="FromTitle"/> only
/// ever returns a literal span of the headline, and <see cref="Sanitize"/> admits
/// a model's answer only after finding it in the story text. That gate is the
/// enforcement — the prompt asks for the same thing, but a prompt is a hint and
/// this string is rendered in the largest type on the card.
/// </remarks>
public static partial class VisualSubject
{
    /// <summary>Capitalised words that carry no identity, keyed case-folded.</summary>
    private static readonly HashSet<string> Stopwords = Fold(
    [
        "bir", "bu", "şu", "yeni", "büyük", "kritik", "önemli", "artık", "ilk", "son",
        "türk", "türkiye", "yapay", "zekâ", "zeka", "teknoloji", "şirket", "şirketi",
        "girişim", "girişimi", "araştırma", "rapor", "milyon", "milyar", "bin",
        "dolar", "euro", "avro", "lira", "sürüm", "versiyon", "saat", "dakika", "yıl",
        "ocak", "şubat", "mart", "nisan", "mayıs", "haziran", "temmuz", "ağustos",
        "eylül", "ekim", "kasım", "aralık", "son dakika",
        "the", "a", "an", "new", "big", "major", "critical", "report", "study",
        "company", "million", "billion", "year", "january", "february", "march",
        "april", "may", "june", "july", "august", "september", "october",
        "november", "december"
    ]);

    /// <summary>
    /// Words that name the category rather than the story. Applied to the model's
    /// answer *and* to our own extraction — otherwise rejecting the model just
    /// re-derives the same generic word from the headline.
    /// </summary>
    private static readonly HashSet<string> Generic = Fold(
    [
        "yapay zekâ", "yapay zeka", "teknoloji", "şirket", "güncelleme", "yazılım",
        "donanım", "güvenlik", "haber", "duyuru", "sürüm", "model", "araştırma",
        "girişim", "yatırım", "ürün", "bilim", "kariyer", "araçlar", "açık kaynak",
        "ai", "ml", "it", "technology", "company", "update", "software", "hardware",
        "security", "news", "announcement", "version", "release", "startup",
        "product", "null", "none", "yok", "bilinmiyor", "n/a", "na"
    ]);

    /// <summary>Units that turn a following number into a quantity, not a version.</summary>
    private static readonly HashSet<string> ScaleWords = Fold(
    [
        "milyon", "milyar", "bin", "trilyon", "kullanıcı", "kullanıcıya", "kişi",
        "kişiye", "adet", "kez", "kat", "yıl", "ay", "gün", "saat", "dakika",
        "tl", "dolar", "euro", "avro", "lira", "gb", "mb", "tb", "kb", "mhz", "ghz",
        "million", "billion", "users", "people", "times", "years", "months", "days"
    ]);

    [GeneratedRegex(@"\bCVE-\d{4}-\d{4,7}\b", RegexOptions.IgnoreCase)]
    private static partial Regex CvePattern();

    /// <summary>Runs against case-folded text, so no IgnoreCase (which mishandles İ).</summary>
    [GeneratedRegex(
        @"(?<cur>[$€₺])?\s?(?<num>\d+(?:[.,]\d+)?)\s*(?<scale>milyon|milyar|million|billion)\b\s*(?<cur2>dolar|dollars?|usd|euro|avro|tl|lira|[$€₺])?")]
    private static partial Regex MoneyPattern();

    /// <summary>A word followed by a version number: "React 20", ".NET 10", "iOS 26".</summary>
    [GeneratedRegex(@"\b(?<name>[A-Za-z][A-Za-z.+#_-]{1,14})\s(?<ver>\d+(?:\.\d+){0,2})\b")]
    private static partial Regex NamedVersionPattern();

    /// <summary>Tokenises on anything that is not a letter, digit or joining punctuation.</summary>
    [GeneratedRegex(@"[A-Za-zÀ-ÿĞğİıŞşÇçÖöÜü0-9][A-Za-zÀ-ÿĞğİıŞşÇçÖöÜü0-9.+#_-]*")]
    private static partial Regex TokenPattern();

    private enum Casing
    {
        /// <summary>Ordinary sentence case: capitalisation carries information.</summary>
        Sentence,

        /// <summary>Most words capitalised — capitalisation says nothing.</summary>
        Title,

        /// <summary>Shouted headline — capitalisation says nothing at all.</summary>
        Upper
    }

    /// <summary>
    /// Returns the headline's subject, or null when nothing in it stands out.
    /// <paramref name="maxLength"/> rejects rather than truncates: a clipped
    /// subject set in display type reads as broken, and the caller has a fallback.
    /// </summary>
    public static string? FromTitle(string? title, int maxLength = 24)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var cve = CvePattern().Match(title);
        if (cve.Success && cve.Value.Length <= maxLength)
        {
            return cve.Value.ToUpperInvariant();
        }

        if (Money(title) is { } money && money.Length <= maxLength)
        {
            return money;
        }

        // "React 20" is one subject, not two, and it is checked before the token
        // scan because a product name at the start of a headline is otherwise
        // indistinguishable from ordinary sentence case.
        if (NamedVersion(title) is { } named && named.Length <= maxLength)
        {
            return named;
        }

        return BestToken(title, maxLength);
    }

    /// <summary>
    /// Admits a model-supplied subject only when it is short, specific, not merely
    /// the category restated, and — the part that matters — actually present in the
    /// story. Otherwise re-derives one from the headline.
    /// </summary>
    /// <param name="corpus">
    /// Story text the candidate must occur in (summary, excerpts). The title is
    /// always searched as well; pass null to search the title alone.
    /// </param>
    public static string? Sanitize(string? candidate, string? title, string? corpus = null, int maxLength = 24)
    {
        var cleaned = candidate?.Trim().Trim('"', '\'', '.', ':', '-', '—', '“', '”', '‘', '’', '«', '»', '*', '_', '`', ' ');

        if (!string.IsNullOrWhiteSpace(cleaned) &&
            cleaned.Length <= maxLength &&
            cleaned.Any(char.IsLetterOrDigit) &&
            !Generic.Contains(FoldCase(cleaned)) &&
            OccursIn(cleaned, title, corpus))
        {
            return cleaned;
        }

        return FromTitle(title, maxLength);
    }

    /// <summary>
    /// True when the term appears in the story, compared case- and diacritic-folded
    /// so Turkish suffixes ("OpenSSH'in") and casing still count as a match.
    /// </summary>
    private static bool OccursIn(string term, string? title, string? corpus)
    {
        var needle = FoldCase(term);
        if (needle.Length == 0)
        {
            return false;
        }

        return FoldCase(title ?? string.Empty).Contains(needle, StringComparison.Ordinal) ||
               (corpus is { Length: > 0 } && FoldCase(corpus).Contains(needle, StringComparison.Ordinal));
    }

    /// <summary>"React 20", "PostgreSQL 18" — a product name carrying a version.</summary>
    private static string? NamedVersion(string title)
    {
        foreach (Match match in NamedVersionPattern().Matches(title))
        {
            var name = match.Groups["name"].Value;
            var version = match.Groups["ver"].Value;

            var looksLikeProduct = char.IsUpper(name[0]) || name.Skip(1).Any(char.IsUpper);
            if (!looksLikeProduct || IsNoise(name))
            {
                continue;
            }

            // "Ocak 2026", "Bakan 2026" — a year is a date, not a version.
            if (version.Length == 4 && int.TryParse(version, out var year) && year is >= 1900 and <= 2100)
            {
                continue;
            }

            // "Uygulama 20 milyon kullanıcı" — the number is a quantity, and the
            // unit that follows is what gives it away.
            if (NextWordIsUnit(title, match.Index + match.Length))
            {
                continue;
            }

            return $"{name} {version}";
        }

        return null;
    }

    private static bool NextWordIsUnit(string title, int from)
    {
        if (from >= title.Length)
        {
            return false;
        }

        var rest = title[from..].TrimStart();
        var end = 0;
        while (end < rest.Length && (char.IsLetter(rest[end]) || rest[end] == '%'))
        {
            end++;
        }

        return end > 0 && ScaleWords.Contains(FoldCase(rest[..end]));
    }

    /// <summary>Compacts a written amount into a display numeral: "20 milyon dolar" → "20M$".</summary>
    private static string? Money(string title)
    {
        // Folded, because RegexOptions.IgnoreCase does not match "MİLYON" to "milyon".
        foreach (Match match in MoneyPattern().Matches(FoldCase(title)))
        {
            var currency = match.Groups["cur"].Value + match.Groups["cur2"].Value;

            // No currency named means this is a count, not an amount — "20 milyon
            // kullanıcı" must never render as "20M$". Keep looking: the amount may
            // appear later in the same headline.
            if (currency.Length == 0)
            {
                continue;
            }

            var symbol = currency.Contains('€') || currency.Contains("euro", StringComparison.Ordinal) ||
                         currency.Contains("avro", StringComparison.Ordinal)
                ? "€"
                : currency.Contains('₺') || currency.Contains("tl", StringComparison.Ordinal) ||
                  currency.Contains("lira", StringComparison.Ordinal)
                    ? "₺"
                    : "$";

            var scale = match.Groups["scale"].Value is "milyar" or "billion" ? "B" : "M";

            return $"{match.Groups["num"].Value.Replace(',', '.')}{scale}{symbol}";
        }

        return null;
    }

    /// <summary>
    /// Scores every token and keeps the most distinctive. What "distinctive" means
    /// depends on the headline's casing: in a shouted or Title Case headline the
    /// capitals carry no signal, so leaning on them picks a random long noun.
    /// </summary>
    private static string? BestToken(string title, int maxLength)
    {
        var casing = DetectCasing(title);
        string? best = null;
        var bestScore = 0;

        foreach (Match match in TokenPattern().Matches(title))
        {
            var token = match.Value.Trim('.', '-', '_', '+', '#');
            if (token.Length is < 2 or > 24 || token.Length > maxLength || IsNoise(token))
            {
                continue;
            }

            var hasDigit = token.Any(char.IsDigit);
            var hasLetter = token.Any(char.IsLetter);
            if (!hasLetter)
            {
                // A bare number is a quantity without context.
                continue;
            }

            var innerUpper = token.Skip(1).Any(char.IsUpper);
            var startsUpper = char.IsUpper(token[0]);

            var score = hasDigit
                // "M5", "o5", "GPT-5" — a model or version designation. The one
                // signal that survives any casing.
                ? 5
                : casing switch
                {
                    // Shouted: nothing but a designation is trustworthy.
                    Casing.Upper => 0,
                    // Title Case: every word is capitalised, so position is all we
                    // have, and the subject is almost always first.
                    Casing.Title => startsUpper ? (match.Index == 0 ? 3 : 1) : 0,
                    _ => (innerUpper, startsUpper) switch
                    {
                        // "OpenSSH", "PostgreSQL" — internal capitals mark a product.
                        (true, _) => 4,
                        // A proper noun mid-sentence, where capitalisation is a choice.
                        (false, true) when match.Index > 0 => 2,
                        // The first word is capitalised by grammar rather than by
                        // choice, so it is only weak evidence — but a headline
                        // that opens with its subject ("Netflix zam yaptı") is
                        // common enough that scoring it zero loses real names.
                        // An inflected Turkish common noun is not a name.
                        (false, true) => IsInflected(token) ? 0 : 1,
                        _ => 0
                    }
                };

            // Strictly greater keeps the earliest of equally-good candidates; a
            // longest-wins tie-break picks inflected common nouns over real names.
            if (score > bestScore)
            {
                best = token;
                bestScore = score;
            }
        }

        return best;
    }

    private static Casing DetectCasing(string title)
    {
        var letters = 0;
        var upper = 0;

        foreach (var ch in title)
        {
            if (!char.IsLetter(ch))
            {
                continue;
            }

            letters++;
            if (char.IsUpper(ch))
            {
                upper++;
            }
        }

        if (letters >= 6 && upper >= letters * 0.85)
        {
            return Casing.Upper;
        }

        var words = title.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 0 && char.IsLetter(w[0]))
            .ToList();

        if (words.Count >= 3 && words.Count(w => char.IsUpper(w[0])) >= words.Count * 0.6)
        {
            return Casing.Title;
        }

        return Casing.Sentence;
    }

    /// <summary>
    /// Turkish plural and case endings. A brand ("Netflix", "Kubernetes") does not
    /// carry them; a common noun at the start of a sentence usually does, which is
    /// the only cheap way to tell the two apart with no dictionary.
    /// </summary>
    private static readonly string[] TurkishSuffixes =
        ["ler", "lar", "leri", "ları", "lerin", "ların", "cılar", "ciler", "lik", "lık"];

    private static bool IsInflected(string token)
    {
        var folded = FoldCase(token);
        return folded.Length > 5 &&
               TurkishSuffixes.Any(suffix => folded.EndsWith(FoldCase(suffix), StringComparison.Ordinal));
    }

    private static bool IsNoise(string token)
    {
        var folded = FoldCase(token);
        return Stopwords.Contains(folded) || Generic.Contains(folded);
    }

    private static HashSet<string> Fold(IEnumerable<string> values) =>
        new(values.Select(FoldCase), StringComparer.Ordinal);

    /// <summary>
    /// Case- and diacritic-folds for comparison. Written out rather than using
    /// ToLowerInvariant because that leaves 'İ' as U+0130, which then matches
    /// neither 'i' nor 'I' — the reason uppercase Turkish words slipped every
    /// filter here before.
    /// </summary>
    private static string FoldCase(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            builder.Append(ch switch
            {
                'İ' or 'I' or 'ı' or 'Î' or 'î' => 'i',
                'Ş' or 'ş' => 's',
                'Ğ' or 'ğ' => 'g',
                'Ç' or 'ç' => 'c',
                'Ö' or 'ö' => 'o',
                'Ü' or 'ü' or 'Û' or 'û' => 'u',
                'Â' or 'â' => 'a',
                _ => char.ToLowerInvariant(ch)
            });
        }

        return builder.ToString();
    }
}
