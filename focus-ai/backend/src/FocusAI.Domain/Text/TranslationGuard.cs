using System.Globalization;
using System.Text.RegularExpressions;

namespace FocusAI.Domain.Text;

/// <summary>Why a candidate translation was refused. <see cref="None"/> means it was accepted.</summary>
public enum TranslationRejection
{
    None,
    Empty,
    LengthRatio,
    LostIdentifier,
    WrongScript,
    Preamble
}

/// <summary>
/// Decides whether a machine translation is safe to publish, and refuses it when
/// it is not.
/// </summary>
/// <remarks>
/// This exists because the measured failure mode of small open translation models
/// on tech news is not awkward phrasing — it is confident nonsense that destroys
/// the one part of a headline that carries the meaning. The Argos en→tr model
/// that LibreTranslate ships turns "React 20 ships a new compiler" into
/// "Reak 20 gemi…" (boats) and "fixes a bug" into "bir boğa düzeltiyor" (a bull).
/// A reader can work around an English sentence. They cannot work around a
/// Turkish sentence that says something the source never said.
///
/// So every check here fails closed: a refused translation means the original
/// text is published untouched. Showing English is a visible, honest gap;
/// showing a corrupted translation is an invisible, dishonest one.
/// </remarks>
public static class TranslationGuard
{
    /// <summary>
    /// Turkish renders the same meaning in noticeably more characters than English
    /// (agglutination plus longer function words), so the upper bound is generous.
    /// The bounds are here to catch collapse and runaway repetition — the two
    /// documented failure modes of these models — not to police style.
    /// </summary>
    private const double MinLengthRatio = 0.45;

    private const double MaxLengthRatio = 2.6;

    /// <summary>Below this, ratio checks are noise: "GPT-5" is a legitimate whole segment.</summary>
    private const int RatioFloorChars = 40;

    /// <summary>Share of letters that may fall outside the Latin/Turkish alphabet.</summary>
    private const double MaxForeignScriptShare = 0.15;

    /// <summary>
    /// Hyphenated or dotted alphanumeric tokens: GPT-5, v1.90.2, CVE-2026-1234,
    /// 1.34, 2026-07-26. Only kept when the token carries a digit, so ordinary
    /// hyphenated English ("in-tree", "open-source") is not frozen in place.
    /// </summary>
    private static readonly Regex CodeToken = new(
        @"\b[A-Za-z0-9]+(?:[.\-/_][A-Za-z0-9]+)+\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Acronyms and product codes: GPU, API, SSH, TB, CVE, M5.</summary>
    private static readonly Regex AcronymLike = new(
        @"\b[A-Z][A-Z0-9]{1,11}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Internal capitals: OpenSSH, JavaScript, PostgreSQL, GitHub, macOS. At least
    /// one lowercase letter is required before the inner capital, otherwise every
    /// ALL-CAPS word matches this too.
    /// </summary>
    private static readonly Regex InnerCapital = new(
        @"\b[A-Za-z][a-z0-9]+[A-Z][A-Za-z0-9]*\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Capitalised words: candidate proper nouns. Filtered by <see cref="SentenceOpeners"/>.</summary>
    private static readonly Regex Capitalised = new(
        @"\b[A-Z][a-z]{2,}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Multi-digit runs. Single digits are excluded: too common to be a signal.</summary>
    private static readonly Regex DigitRun = new(
        @"[0-9]{2,}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// A model asked for a translation sometimes answers about the translation
    /// instead. Anchored to the start so a legitimate mid-sentence "çeviri" survives.
    /// </summary>
    private static readonly Regex PreambleLine = new(
        @"^\s*(?:here(?:'s| is)\b[^\n:]*:|sure[,!]?\s+|okay[,!]?\s+|of course[,!]?\s+|translation\b[^\n:]*:|translated\b[^\n:]*:|çeviri\b[^\n:]*:|i̇şte\b[^\n:]*:|iste\b[^\n:]*:|tabii[,!]?\s+|elbette[,!]?\s+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Capitalised words that are ordinary English rather than names, and so are
    /// free to change under translation.
    /// </summary>
    /// <remarks>
    /// A capitalised word mid-sentence is almost certainly a name. At the start of
    /// a sentence it is ambiguous — "React ships…" and "The compiler ships…" look
    /// identical to a regex — and that is exactly where product names live in a
    /// news lede, so skipping sentence-initial words entirely would leave the most
    /// important token in the segment unprotected. This list resolves the ambiguity
    /// the only way it can be resolved without a dictionary.
    ///
    /// Two groups have to be here even though they are grammatically proper nouns:
    /// months and weekdays, which Turkish genuinely translates ("January" → "Ocak"),
    /// and the common demonyms, which it also translates ("English" → "İngilizce").
    /// They turn up constantly in news copy, and protecting them would reject a
    /// correct translation on nearly every dated story. Losing one of them costs a
    /// word; losing a product name costs the meaning.
    ///
    /// Elsewhere the list is deliberately allowed to be incomplete. A word missing
    /// from it means an over-strict guard, which refuses a translation and publishes
    /// the English source — the behaviour that existed before translation was wired
    /// in. The opposite mistake publishes a corrupted product name to a reader who
    /// has no way to tell.
    /// </remarks>
    private static readonly HashSet<string> NotProperNouns = new(StringComparer.OrdinalIgnoreCase)
    {
        // Months and weekdays: Turkish has its own names for all of them.
        "january", "february", "march", "april", "may", "june", "july", "august",
        "september", "october", "november", "december",
        "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday",
        // Demonyms and languages, likewise.
        "english", "turkish", "chinese", "japanese", "korean", "german", "french",
        "spanish", "russian", "indian", "american", "european", "british", "dutch",
        "italian", "brazilian", "canadian", "australian",
        "the", "and", "but", "for", "nor", "yet", "not", "all", "any", "both", "each",
        "this", "that", "these", "those", "there", "here", "they", "them", "their",
        "she", "her", "his", "him", "its", "our", "your", "you", "who", "whom", "whose",
        "what", "when", "where", "which", "while", "why", "how",
        "with", "without", "within", "from", "into", "onto", "over", "under", "after",
        "before", "during", "since", "until", "between", "among", "against", "about",
        "across", "along", "around", "behind", "beyond", "despite", "except", "toward",
        "though", "although", "because", "however", "meanwhile", "instead", "moreover",
        "also", "still", "already", "again", "now", "then", "today", "yesterday",
        "tomorrow", "recently", "currently", "previously", "finally", "first",
        "second", "third", "last", "next", "earlier", "later", "one", "two", "three",
        "four", "five", "six", "seven", "eight", "nine", "ten",
        "new", "old", "more", "most", "less", "least", "many", "much", "some",
        "several", "other", "another", "every", "same", "such", "only", "just",
        "according", "following", "starting", "beginning", "adding", "using",
        "unlike", "like", "per", "via", "amid", "plus", "versus",
        "was", "were", "has", "have", "had", "will", "would", "can", "could",
        "should", "may", "might", "must", "does", "did", "are", "and",
        "his", "she", "it", "we", "he",
        // Common English subjects in tech copy. Translating these is correct, so
        // requiring them to survive would reject good translations.
        "developers", "users", "researchers", "engineers", "companies", "customers",
        "security", "support", "version", "release", "update", "updates", "software",
        "hardware", "data", "code", "team", "teams", "company", "project", "projects",
        "users", "people", "government", "governments", "researchers"
    };

    /// <summary>
    /// Accepts <paramref name="candidate"/> as a translation of <paramref name="source"/>,
    /// or refuses it with a reason. <paramref name="accepted"/> is the text to publish
    /// on success — it may differ from the candidate, since a leading preamble is
    /// stripped rather than treated as fatal.
    /// </summary>
    public static TranslationRejection Inspect(string source, string? candidate, out string accepted)
    {
        accepted = source;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return TranslationRejection.Empty;
        }

        var text = candidate.Trim();

        // Stripped, not rejected: the translation itself is usually fine, and the
        // preamble is a formatting slip rather than a meaning error. Looping
        // because they stack — "Sure, here is the translation:" is two of them.
        for (var pass = 0; pass < 3; pass++)
        {
            var stripped = PreambleLine.Replace(text, string.Empty, 1).Trim();
            if (stripped.Length == text.Length)
            {
                break;
            }

            text = stripped;
        }

        // Trailing colon means the model announced a translation and never gave one.
        if (text.Length == 0 || text.TrimEnd().EndsWith(':'))
        {
            return TranslationRejection.Preamble;
        }

        if (string.IsNullOrWhiteSpace(source))
        {
            accepted = text;
            return TranslationRejection.None;
        }

        if (!ScriptLooksRight(text))
        {
            return TranslationRejection.WrongScript;
        }

        // Long enough for the ratio to mean something: short segments are mostly
        // identifiers, where a 3× swing is normal and harmless.
        if (source.Length >= RatioFloorChars)
        {
            var ratio = text.Length / (double)source.Length;
            if (ratio < MinLengthRatio || ratio > MaxLengthRatio)
            {
                return TranslationRejection.LengthRatio;
            }
        }

        foreach (var identifier in Identifiers(source))
        {
            // Containment, not token equality: Turkish suffixes attach directly
            // ("OpenSSH'in", "10.2'de"), so the identifier survives as a substring.
            if (!text.Contains(identifier, StringComparison.OrdinalIgnoreCase))
            {
                return TranslationRejection.LostIdentifier;
            }
        }

        accepted = text;
        return TranslationRejection.None;
    }

    /// <summary>
    /// Tokens whose exact form carries the meaning and must survive translation:
    /// product names, version numbers, acronyms, CVE ids, figures.
    /// </summary>
    public static IReadOnlyList<string> Identifiers(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Collect(Regex pattern, Func<Match, bool>? keep = null)
        {
            foreach (Match match in pattern.Matches(source))
            {
                if ((keep is null || keep(match)) && seen.Add(match.Value))
                {
                    found.Add(match.Value);
                }
            }
        }

        // Digit required: otherwise ordinary hyphenated English ("in-tree",
        // "open-source") would have to survive translation verbatim.
        Collect(CodeToken, m => m.Value.Any(char.IsDigit));

        // An all-caps headline would make every word an "acronym" and reject every
        // translation of it. Same trap VisualSubject hits when scoring headlines.
        var allCaps = IsAllCaps(source);
        if (!allCaps)
        {
            Collect(AcronymLike);
            Collect(Capitalised, IsProperNoun);
        }

        Collect(InnerCapital);
        Collect(DigitRun);

        return found;
    }

    /// <summary>
    /// Whether a capitalised word is a name rather than an ordinary word that
    /// happens to open a sentence.
    /// </summary>
    private static bool IsProperNoun(Match match) => !NotProperNouns.Contains(match.Value);

    /// <summary>
    /// Guards against the model answering in the wrong language entirely — the
    /// realistic risk with a multilingual model that supports 30-plus targets and
    /// takes the target from the prompt.
    /// </summary>
    private static bool ScriptLooksRight(string text)
    {
        var letters = 0;
        var foreign = 0;

        foreach (var rune in text)
        {
            if (!char.IsLetter(rune))
            {
                continue;
            }

            letters++;

            // Latin-1 plus Latin Extended-A covers English and every Turkish
            // letter (ç ğ ı İ ö ş ü). Anything past it is another script.
            if (rune > 'ſ')
            {
                foreign++;
            }
        }

        return letters == 0 || foreign / (double)letters <= MaxForeignScriptShare;
    }

    private static bool IsAllCaps(string text)
    {
        var upper = 0;
        var lower = 0;

        foreach (var rune in text)
        {
            if (char.IsUpper(rune))
            {
                upper++;
            }
            else if (char.IsLower(rune))
            {
                lower++;
            }
        }

        return upper > 0 && lower * 4 < upper;
    }

    /// <summary>
    /// Splits text into translation-sized chunks on sentence boundaries. A small
    /// model degrades sharply on long inputs, and a chunk that is refused costs
    /// only its own sentences rather than the whole field.
    /// </summary>
    public static IReadOnlyList<string> Chunk(string text, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var trimmed = text.Trim();
        if (trimmed.Length <= maxChars)
        {
            return [trimmed];
        }

        var chunks = new List<string>();
        var current = new System.Text.StringBuilder();

        foreach (var sentence in SplitSentences(trimmed))
        {
            // A single sentence longer than the budget is emitted whole rather than
            // cut: a half sentence cannot be translated into anything meaningful.
            if (current.Length > 0 && current.Length + sentence.Length + 1 > maxChars)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
            }

            if (current.Length > 0)
            {
                current.Append(' ');
            }

            current.Append(sentence);
        }

        if (current.Length > 0)
        {
            chunks.Add(current.ToString().Trim());
        }

        return chunks.Count > 0 ? chunks : [trimmed];
    }

    /// <summary>
    /// Whether the character at <paramref name="i"/> actually ends a sentence.
    /// </summary>
    /// <remarks>
    /// A bare "is it a period" test breaks on two things this pipeline sees
    /// constantly: decimals ("Kubernetes 1.34") and Turkish ordinals, which are
    /// written exactly like a sentence end — "Bu 1. cümledir". Splitting there
    /// produces fragments no translator can do anything with.
    /// </remarks>
    private static bool EndsSentence(string text, int i)
    {
        var c = text[i];

        if (c is '!' or '?' or '\n')
        {
            return true;
        }

        if (c != '.')
        {
            return false;
        }

        // Decimal: "1.34".
        if (i + 1 < text.Length && char.IsDigit(text[i + 1]))
        {
            return false;
        }

        // Ordinal: a digit before the period and a lowercase word after it. A real
        // sentence end is followed by a capital.
        if (i > 0 && char.IsDigit(text[i - 1]))
        {
            var j = i + 1;
            while (j < text.Length && text[j] is ' ' or '\t')
            {
                j++;
            }

            if (j < text.Length && char.IsLower(text[j]))
            {
                return false;
            }
        }

        return true;
    }

    private static IEnumerable<string> SplitSentences(string text)
    {
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (!EndsSentence(text, i))
            {
                continue;
            }

            // Consume the run of terminators so "?!" does not produce an empty piece.
            var end = i;
            while (end + 1 < text.Length && text[end + 1] is '.' or '!' or '?' or '"' or '\'' or '\n')
            {
                end++;
            }

            var piece = text[start..(end + 1)].Trim();
            if (piece.Length > 0)
            {
                yield return piece;
            }

            start = end + 1;
            i = end;
        }

        var tail = start < text.Length ? text[start..].Trim() : string.Empty;
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    /// <summary>
    /// True when text in this language needs translating for a Turkish reader.
    /// Tolerates the tag forms that turn up in feeds ("tr-TR", "TR", " tr ").
    /// </summary>
    public static bool NeedsTurkish(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            // An unknown language is treated as foreign. The guard above is what
            // makes that safe: a needless translation attempt on Turkish text
            // either round-trips harmlessly or is refused.
            return true;
        }

        var tag = language.Trim();
        var separator = tag.IndexOfAny(['-', '_']);
        if (separator > 0)
        {
            tag = tag[..separator];
        }

        return !string.Equals(tag, "tr", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(tag, "tur", StringComparison.OrdinalIgnoreCase) &&
               !CultureMatchesTurkish(tag);
    }

    /// <summary>
    /// Letters that are effectively unique to Turkish among the languages this
    /// pipeline sees. ö, ü and ç are deliberately excluded — German and French
    /// share them, and a German feed labelled "en" would then read as Turkish.
    /// </summary>
    private static readonly char[] TurkishLetters = ['ğ', 'Ğ', 'ş', 'Ş', 'ı', 'İ'];

    private static readonly string[] TurkishWords =
        ["için", "olarak", "ile", "ancak", "sonra", "üzerinde", "arasında", "göre", "kadar"];

    /// <summary>
    /// Whether text is already Turkish, judged from the text itself.
    /// </summary>
    /// <remarks>
    /// The language column is not trustworthy enough to be the only gate. It is
    /// copied from the source registration at ingestion, so a source that was
    /// registered with the wrong language — TCMB was, until this was fixed — has
    /// every one of its stored articles mislabelled, and correcting the source row
    /// does not correct them. Reading the text costs nothing and is right about the
    /// rows the column is wrong about.
    /// </remarks>
    public static bool LooksTurkish(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var distinctive = text.Count(c => Array.IndexOf(TurkishLetters, c) >= 0);
        if (distinctive >= 2 && text.Length >= 30)
        {
            return true;
        }

        var lowered = text.ToLowerInvariant();
        var hits = TurkishWords.Count(word =>
            lowered.Contains($" {word} ", StringComparison.Ordinal) ||
            lowered.StartsWith($"{word} ", StringComparison.Ordinal));

        return hits >= 2;
    }

    private static bool CultureMatchesTurkish(string tag)
    {
        try
        {
            var culture = CultureInfo.GetCultureInfo(tag);
            return culture.TwoLetterISOLanguageName.Equals("tr", StringComparison.OrdinalIgnoreCase);
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }
}
