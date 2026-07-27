using System.Text;
using System.Text.RegularExpressions;
using FocusAI.Domain.Text;

namespace FocusAI.Domain.Speech;

/// <summary>
/// Turns a story into something a Turkish speech engine can read aloud.
/// </summary>
/// <remarks>
/// Two jobs, and the second one is the reason this lives in the domain layer
/// rather than in the player.
///
/// <b>What gets read.</b> Not the page — the story's own structured fields, in
/// the order a listener needs them: what happened, then why it matters, then the
/// points. A section that only repeats the headline is dropped, because hearing
/// the same sentence twice is far more grating than reading it twice.
///
/// <b>How it is written for speech.</b> Turkish engines mangle a specific and
/// predictable set of things: acronyms read as words, units read as letters,
/// currency signs read as nothing, and markdown read as punctuation. Those are
/// fixed here. Sentences are then split so the player can track progress, change
/// speed without restarting, and stay clear of the long-utterance stalls that
/// iOS is prone to.
///
/// Pure and deterministic, so the same story always produces the same script —
/// which is what lets a server-side engine cache audio against it later.
/// </remarks>
public static partial class SpeechScript
{
    /// <summary>
    /// Past roughly this many characters an utterance becomes a liability: iOS
    /// stalls on long ones, and a speed change has to replay the whole thing.
    /// </summary>
    private const int MaxChunkLength = 220;

    /// <summary>Below this a fragment is not worth its own pause.</summary>
    private const int MinChunkLength = 3;

    [GeneratedRegex(@"\[(?<text>[^\]]+)\]\((?<url>[^)]+)\)")]
    private static partial Regex MarkdownLink();

    /// <summary>
    /// A URL, and the Turkish locative that usually trails it. Both go, because
    /// removing only the address leaves "Detaylar adresinde" — a sentence with
    /// its subject missing.
    /// </summary>
    [GeneratedRegex(
        @"(?:https?://\S+|www\.\S+)(?:\s+(?:adreslerinde|adresinden|adresinde|adresine|sayfasından|sayfasında|bağlantısında|linkinde))?")]
    private static partial Regex BareUrl();

    /// <summary>Decoration that carries meaning on screen and noise in audio.</summary>
    [GeneratedRegex(@"[*_`#>|~^▓░›‹→←⇒⚠✓✕✔✖•·─═…]+")]
    private static partial Regex Decoration();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    /// <summary>A number followed by a byte/frequency/time unit, spaced or not.</summary>
    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)\s*(?<unit>KB|MB|GB|TB|PB|GHz|MHz|kHz|Hz|kW|MW|GW|ms|km)\b")]
    private static partial Regex NumberWithUnit();

    public static IReadOnlyList<SpeechChunk> Build(SpeechSource source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var chunks = new List<SpeechChunk>();
        var spoken = new List<string>();

        void AddSection(string? text, SpeechChunkKind kind, string? heading = null)
        {
            if (string.IsNullOrWhiteSpace(text) || RepeatsSomethingAlreadySaid(text, spoken))
            {
                return;
            }

            if (heading is not null)
            {
                chunks.Add(new SpeechChunk(heading, SpeechChunkKind.Heading));
            }

            foreach (var sentence in Sentences(text))
            {
                chunks.Add(new SpeechChunk(sentence, kind));
            }

            spoken.Add(text);
        }

        // The headline is its own utterance so the listener gets a beat before the
        // body starts, the way a bulletin reads.
        foreach (var sentence in Sentences(source.Title))
        {
            chunks.Add(new SpeechChunk(sentence, SpeechChunkKind.Title));
        }

        spoken.Add(source.Title);

        AddSection(source.Dek, SpeechChunkKind.Dek);
        AddSection(source.Summary, SpeechChunkKind.Summary);
        AddSection(source.WhyItMatters, SpeechChunkKind.WhyItMatters, "Neden önemli.");
        AddSection(source.WhoIsAffected, SpeechChunkKind.WhoIsAffected, "Kimleri etkiliyor.");

        var points = source.KeyPoints
            .Where(point => !string.IsNullOrWhiteSpace(point) && !RepeatsSomethingAlreadySaid(point, spoken))
            .ToList();

        if (points.Count > 0)
        {
            chunks.Add(new SpeechChunk("Öne çıkanlar.", SpeechChunkKind.Heading));

            foreach (var point in points)
            {
                // One bullet, one utterance — a list read as continuous prose loses
                // the thing that made it a list.
                foreach (var sentence in Sentences(point))
                {
                    chunks.Add(new SpeechChunk(sentence, SpeechChunkKind.KeyPoint));
                }

                spoken.Add(point);
            }
        }

        AddSection(source.WhatShouldIDo, SpeechChunkKind.WhatShouldIDo, "Ne yapmalıyım.");

        return chunks;
    }

    /// <summary>
    /// Rewrites for the ear, then splits into utterances. Public because the
    /// splitting rules are the part most worth testing directly.
    /// </summary>
    public static IReadOnlyList<string> Sentences(string? text)
    {
        var speakable = Speakable(text);
        if (speakable.Length == 0)
        {
            return [];
        }

        var sentences = new List<string>();

        foreach (var sentence in Split(speakable))
        {
            sentences.AddRange(Shorten(sentence));
        }

        return sentences
            .Select(Terminate)
            .Where(sentence => sentence.Length >= MinChunkLength)
            .ToList();
    }

    /// <summary>Everything that changes how the text sounds, before it is split.</summary>
    public static string Speakable(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var result = MarkdownLink().Replace(text, "${text}");

        // A read-aloud URL is unlistenable and never carries the point. Naming it
        // "bağlantıda" keeps the sentence standing where deleting it outright
        // would not.
        result = BareUrl().Replace(result, "bağlantıda");

        // Before the splitter sees them: expanding these removes the trailing dot
        // that would otherwise have to be defended as a non-boundary.
        result = ExpandAbbreviation(result, "vb.", "ve benzeri");
        result = ExpandAbbreviation(result, "vs.", "vesaire");
        result = ExpandAbbreviation(result, "örn.", "örneğin");
        result = ExpandAbbreviation(result, "bkz.", "bakınız");

        result = SpeechLexicon.Percentage().Replace(result, match => $"yüzde {match.Groups["value"].Value}");

        result = SpeechLexicon.ScaledCurrency().Replace(result, match => Money(
            match.Groups["value"].Value,
            match.Groups["scale"].Success ? match.Groups["scale"].Value : null,
            match.Groups["currency"].Value[0]));

        result = SpeechLexicon.LeadingCurrency().Replace(result, match => Money(
            match.Groups["value"].Value,
            match.Groups["scale"].Success ? match.Groups["scale"].Value : null,
            match.Groups["currency"].Value[0]));

        result = NumberWithUnit().Replace(result, match =>
            $"{match.Groups["value"].Value} {SpeechLexicon.Units[match.Groups["unit"].Value]}");

        foreach (var (acronym, spoken) in SpeechLexicon.Acronyms)
        {
            // Case-sensitive and whole-word: the point is to catch the all-caps
            // token, and many engines spell those out letter by letter anyway.
            result = Regex.Replace(result, $@"\b{Regex.Escape(acronym)}\b", spoken, RegexOptions.None);
        }

        result = result.Replace("&", " ve ");
        result = Decoration().Replace(result, " ");

        return Whitespace().Replace(result, " ").Trim();
    }

    private static string Money(string value, string? scale, char currency)
    {
        var parts = new List<string> { value };

        var scaleName = SpeechLexicon.ScaleName(scale);
        if (scaleName.Length > 0)
        {
            parts.Add(scaleName);
        }

        parts.Add(SpeechLexicon.CurrencyName(currency));
        return string.Join(' ', parts);
    }

    /// <summary>
    /// Expands an abbreviation, keeping whatever case it was written in. "Bkz."
    /// opening a sentence has to come back as "Bakınız", or the splitter sees a
    /// lowercase start and the sentence merges into its neighbour.
    /// </summary>
    private static string ExpandAbbreviation(string text, string abbreviation, string replacement) =>
        Regex.Replace(
            text,
            $@"(?<![\p{{L}}]){Regex.Escape(abbreviation)}",
            match => char.IsUpper(match.Value[0])
                ? char.ToUpper(replacement[0], TurkishCulture) + replacement[1..]
                : replacement,
            RegexOptions.IgnoreCase);

    /// <summary>
    /// Turkish casing, explicitly. The invariant culture maps "i" to "I" rather
    /// than "İ", which is the wrong letter and reads as a different word.
    /// </summary>
    private static readonly System.Globalization.CultureInfo TurkishCulture =
        System.Globalization.CultureInfo.GetCultureInfo("tr-TR");

    /// <summary>
    /// Splits on terminators, refusing the ones that do not end a sentence.
    /// </summary>
    /// <remarks>
    /// Turkish makes this harder than English in one specific way: an ordinal is
    /// written "3. çeyrek", so a digit followed by a period is usually *not* a
    /// boundary. Combined with decimals, version numbers and the standard
    /// abbreviation list, the safe default is to refuse — an over-long utterance
    /// merely sounds run-on, while a wrong split drops a pause into the middle of
    /// a phrase, which sounds broken.
    /// </remarks>
    private static IEnumerable<string> Split(string text)
    {
        var start = 0;

        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is not ('.' or '!' or '?'))
            {
                continue;
            }

            // Run past "!?" and "..." so the whole cluster is one boundary.
            var end = index;
            while (end + 1 < text.Length && text[end + 1] is '.' or '!' or '?')
            {
                end++;
            }

            if (!IsBoundary(text, index, end))
            {
                index = end;
                continue;
            }

            var sentence = text[start..(end + 1)].Trim();
            if (sentence.Length > 0)
            {
                yield return sentence;
            }

            start = end + 1;
            index = end;
        }

        var tail = text[start..].Trim();
        if (tail.Length > 0)
        {
            yield return tail;
        }
    }

    private static bool IsBoundary(string text, int first, int last)
    {
        // Nothing after it: end of text is always a boundary.
        var next = last + 1;
        while (next < text.Length && text[next] == ' ')
        {
            next++;
        }

        if (next >= text.Length)
        {
            return true;
        }

        // A terminator glued to the next word is punctuation inside a token
        // ("3.5", "Node.js"), not the end of a sentence.
        if (last + 1 < text.Length && text[last + 1] != ' ')
        {
            return false;
        }

        if (!StartsSentence(text[next]))
        {
            return false;
        }

        if (text[first] != '.')
        {
            return true;
        }

        var word = WordBefore(text, first);

        // "3. çeyrek", "1. sürüm" — an ordinal, and the capital that follows is
        // no help because Turkish capitalises plenty of common nouns in titles.
        if (word.Length > 0 && word.All(char.IsDigit))
        {
            return false;
        }

        // "A. Yılmaz"
        if (word.Length == 1 && char.IsUpper(word[0]))
        {
            return false;
        }

        return !SpeechLexicon.Abbreviations.Contains(word);
    }

    private static string WordBefore(string text, int dotIndex)
    {
        var end = dotIndex;
        var start = end;

        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '.'))
        {
            start--;
        }

        return text[start..end].TrimEnd('.');
    }

    /// <summary>
    /// Turkish adds Ç, Ğ, İ, Ö, Ş, Ü to the uppercase set, and İ is the one that
    /// breaks naive checks — <c>char.IsUpper</c> handles it, a range check does not.
    /// </summary>
    private static bool StartsSentence(char character) =>
        char.IsUpper(character) || char.IsDigit(character) || character is '"' or '“' or '(' or '\'';

    /// <summary>
    /// Breaks an over-long sentence at its last comma before the limit. Falls back
    /// to a hard cut only when there is no punctuation to lean on.
    /// </summary>
    private static IEnumerable<string> Shorten(string sentence)
    {
        while (sentence.Length > MaxChunkLength)
        {
            var cut = sentence.LastIndexOfAny([',', ';', ':'], MaxChunkLength - 1);

            if (cut < MaxChunkLength / 3)
            {
                cut = sentence.LastIndexOf(' ', MaxChunkLength - 1);
            }

            if (cut <= 0)
            {
                break;
            }

            yield return sentence[..(cut + 1)].Trim();
            sentence = sentence[(cut + 1)..].TrimStart();
        }

        if (sentence.Length > 0)
        {
            yield return sentence;
        }
    }

    /// <summary>
    /// Gives every utterance a terminator. Engines pause on punctuation, and a
    /// chunk cut at a comma otherwise runs straight into the next one.
    /// </summary>
    private static string Terminate(string sentence)
    {
        var trimmed = sentence.TrimEnd();

        if (trimmed.Length == 0)
        {
            return trimmed;
        }

        return trimmed[^1] switch
        {
            '.' or '!' or '?' or ':' => trimmed,
            ',' or ';' => trimmed[..^1] + ",",
            _ => trimmed + "."
        };
    }

    /// <summary>
    /// Whether this section says what an earlier one already said. Compared after
    /// normalisation, so punctuation and case do not hide a repeat.
    /// </summary>
    private static bool RepeatsSomethingAlreadySaid(string candidate, IReadOnlyList<string> spoken)
    {
        var normalized = TextNormalizer.Normalize(candidate);
        if (normalized.Length == 0)
        {
            return true;
        }

        foreach (var previous in spoken)
        {
            var other = TextNormalizer.Normalize(previous);
            if (other.Length == 0)
            {
                continue;
            }

            if (other.Contains(normalized, StringComparison.Ordinal) ||
                normalized.Contains(other, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}

public enum SpeechChunkKind
{
    Title = 0,
    Dek = 1,
    Summary = 2,
    Heading = 3,
    WhyItMatters = 4,
    WhoIsAffected = 5,
    KeyPoint = 6,
    WhatShouldIDo = 7
}

/// <summary>One utterance, and which part of the story it came from.</summary>
public sealed record SpeechChunk(string Text, SpeechChunkKind Kind);

/// <summary>The story fields the script is built from.</summary>
public sealed record SpeechSource
{
    public required string Title { get; init; }

    public string? Dek { get; init; }

    public string? Summary { get; init; }

    public string? WhyItMatters { get; init; }

    public string? WhoIsAffected { get; init; }

    public string? WhatShouldIDo { get; init; }

    public IReadOnlyList<string> KeyPoints { get; init; } = [];
}

/// <summary>An estimate of how long the script takes to read, at rate 1.</summary>
public static class SpeechDuration
{
    /// <summary>
    /// Turkish is agglutinative, so it runs fewer words per minute than English
    /// at the same intelligibility. 155 is a conservative middle for a synthetic
    /// voice at rate 1; the figure is only ever shown as "about".
    /// </summary>
    private const double WordsPerMinute = 155d;

    public static int SecondsFor(IReadOnlyList<SpeechChunk> chunks)
    {
        var words = chunks.Sum(chunk => chunk.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);

        // A beat between utterances, which at this many chunks is not a rounding error.
        var pauses = chunks.Count * 0.35d;

        return (int)Math.Ceiling(words / WordsPerMinute * 60d + pauses);
    }
}
