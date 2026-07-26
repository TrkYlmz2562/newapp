using System.Text.RegularExpressions;

namespace FocusAI.Domain.Text;

/// <summary>
/// Picks the one term a headline is actually *about* — "M5", "OpenSSH", "20M$",
/// "CVE-2026-1234" — so a card can show the subject instead of generic art.
/// </summary>
/// <remarks>
/// This is deliberately extraction, never invention: every candidate is a literal
/// token from the headline. It exists because the LLM path is optional — the
/// default provider is Disabled and any failed call degrades to the extractive
/// fallback, so without a deterministic subject most cards would render blank.
/// </remarks>
public static partial class VisualSubject
{
    /// <summary>Sentence-initial and adjectival words that are capitalised but say nothing.</summary>
    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Bir", "Bu", "Şu", "O", "Yeni", "Büyük", "Kritik", "Önemli", "Artık", "Ilk", "İlk",
        "Son", "Türk", "Türkiye", "Yapay", "Zekâ", "Zeka", "Teknoloji", "Şirket", "Şirketi",
        "Girişim", "Girişimi", "Araştırma", "Rapor", "Milyon", "Milyar", "Dolar", "Sürüm",
        "The", "A", "An", "New", "Big", "Major", "Critical", "Report", "Study", "Company"
    };

    [GeneratedRegex(@"\bCVE-\d{4}-\d{4,7}\b", RegexOptions.IgnoreCase)]
    private static partial Regex CvePattern();

    /// <summary>"20 milyon dolar", "1,5 milyar dolar", "$20 milyon", "20M$".</summary>
    [GeneratedRegex(
        @"(?<cur>[$€])?\s?(?<num>\d+(?:[.,]\d+)?)\s*(?<scale>milyon|milyar|million|billion|[MB])\b\s*(?<cur2>dolar|dollars?|usd|euro|avro|[$€])?",
        RegexOptions.IgnoreCase)]
    private static partial Regex MoneyPattern();

    /// <summary>A word followed by a version number: "React 20", ".NET 10", "iOS 26".</summary>
    [GeneratedRegex(@"\b(?<name>[A-Za-z][A-Za-z.+#_-]{1,14})\s(?<ver>\d+(?:\.\d+){0,2})\b")]
    private static partial Regex NamedVersionPattern();

    /// <summary>Tokenises on anything that is not a letter, digit or joining punctuation.</summary>
    [GeneratedRegex(@"[A-Za-zÀ-ÿĞğİıŞşÇçÖöÜü0-9][A-Za-zÀ-ÿĞğİıŞşÇçÖöÜü0-9.+#_-]*")]
    private static partial Regex TokenPattern();

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
        // scan because a product name at the very start of a headline is
        // indistinguishable from ordinary sentence case on its own.
        if (NamedVersion(title) is { } named && named.Length <= maxLength)
        {
            return named;
        }

        return BestToken(title, maxLength);
    }

    /// <summary>
    /// Accepts a model-supplied subject only when it is short, specific and not
    /// one of the generic words the prompt forbids; otherwise re-derives one from
    /// the headline. A model that answers "yapay zekâ" or "güncelleme" has given
    /// us the category, which the card already shows.
    /// </summary>
    public static string? Sanitize(string? candidate, string? title, int maxLength = 24)
    {
        var cleaned = candidate?.Trim().Trim('"', '\'', '.', ':', '-', '—');

        if (!string.IsNullOrWhiteSpace(cleaned) &&
            cleaned.Length <= maxLength &&
            cleaned.Any(char.IsLetterOrDigit) &&
            !Generic.Contains(cleaned))
        {
            return cleaned;
        }

        return FromTitle(title, maxLength);
    }

    /// <summary>Words that name the category rather than the story.</summary>
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "yapay zekâ", "yapay zeka", "teknoloji", "şirket", "güncelleme", "yazılım",
        "donanım", "güvenlik", "haber", "duyuru", "sürüm", "model", "araştırma",
        "girişim", "yatırım", "ürün", "bilim", "kariyer", "araçlar", "açık kaynak",
        "ai", "technology", "company", "update", "software", "hardware", "security",
        "news", "announcement", "version", "release", "startup", "product", "null"
    };

    /// <summary>"React 20", "PostgreSQL 18" — a product name carrying a version.</summary>
    private static string? NamedVersion(string title)
    {
        foreach (Match match in NamedVersionPattern().Matches(title))
        {
            var name = match.Groups["name"].Value;

            // Requiring a capital keeps "sürüm 3" and "adım 2" out; the stopword
            // list keeps the capitalised-but-empty words out.
            var looksLikeProduct = char.IsUpper(name[0]) || name.Skip(1).Any(char.IsUpper);
            if (!looksLikeProduct || Stopwords.Contains(name))
            {
                continue;
            }

            return $"{name} {match.Groups["ver"].Value}";
        }

        return null;
    }

    /// <summary>Compacts a written amount into a display numeral: "20 milyon dolar" → "20M$".</summary>
    private static string? Money(string title)
    {
        var match = MoneyPattern().Match(title);
        if (!match.Success)
        {
            return null;
        }

        var currency = match.Groups["cur"].Value + match.Groups["cur2"].Value;
        var symbol = currency.Contains('€') || currency.Contains("euro", StringComparison.OrdinalIgnoreCase) ||
                     currency.Contains("avro", StringComparison.OrdinalIgnoreCase)
            ? "€"
            : currency.Length > 0
                ? "$"
                : string.Empty;

        // No currency named anywhere means this is a count, not an amount —
        // "20 milyon kullanıcı" must not render as "20M$".
        if (symbol.Length == 0)
        {
            return null;
        }

        var scale = match.Groups["scale"].Value.ToLowerInvariant() switch
        {
            "milyar" or "billion" or "b" => "B",
            _ => "M"
        };

        return $"{match.Groups["num"].Value.Replace(',', '.')}{scale}{symbol}";
    }

    /// <summary>
    /// Scores every token and keeps the most distinctive: things with digits in
    /// them beat internal capitals, which beat ordinary capitalised words.
    /// </summary>
    private static string? BestToken(string title, int maxLength)
    {
        string? best = null;
        var bestScore = 0;

        foreach (Match match in TokenPattern().Matches(title))
        {
            var token = match.Value.Trim('.', '-', '_', '+', '#');
            if (token.Length is < 2 or > 24 || token.Length > maxLength || Stopwords.Contains(token))
            {
                continue;
            }

            var hasDigit = token.Any(char.IsDigit);
            var hasLetter = token.Any(char.IsLetter);
            var startsUpper = char.IsUpper(token[0]);
            var innerUpper = token.Skip(1).Any(char.IsUpper);

            var score = (hasDigit, hasLetter, innerUpper, startsUpper) switch
            {
                // "M5", "o5", "GPT-5", "9.9p2" — a model or version designation.
                (true, true, _, _) => 5,
                // "OpenSSH", "PostgreSQL", "OpenAI" — internal capitals mark a product.
                (false, true, true, _) => 4,
                // An ordinary proper noun. Only counts away from the first word,
                // where capitalisation is just sentence case.
                (false, true, false, true) when match.Index > 0 => 2,
                _ => 0
            };

            // A bare number is a quantity without context; it needs a unit to mean anything.
            if (!hasLetter)
            {
                score = 0;
            }

            if (score > bestScore || (score == bestScore && score > 0 && token.Length > (best?.Length ?? 0)))
            {
                best = token;
                bestScore = score;
            }
        }

        return best;
    }
}
