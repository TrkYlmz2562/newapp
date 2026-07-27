using System.Text.RegularExpressions;

namespace FocusAI.Domain.Speech;

/// <summary>
/// The substitutions that stand between a Turkish speech engine and something
/// worth listening to.
/// </summary>
/// <remarks>
/// Deliberately narrow. It covers the cases where a Turkish voice is
/// unambiguously wrong — an acronym read as though it were a Turkish word, a
/// unit read as letters, a currency symbol read as nothing at all — and stops
/// there.
///
/// Brand names are left alone on purpose. "Google" or "GitHub" come out of a
/// Turkish voice imperfectly but recognisably, and a transliteration is a guess
/// about one particular engine's phonetics that could easily be worse than what
/// it replaced. Acronyms are not a guess: "API" read as the Turkish word *api*
/// is simply not the word, and spelling it out is right on every engine.
/// </remarks>
public static partial class SpeechLexicon
{
    /// <summary>
    /// Acronyms spelled out with Turkish letter names. Matched whole-word and
    /// case-sensitively, so "AI" is expanded but "Ai" inside a slug is not.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Acronyms =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AI"] = "ey ay",
            ["API"] = "ey pi ay",
            ["AWS"] = "ey dabılyu es",
            ["CDN"] = "si di en",
            ["CLI"] = "si el ay",
            ["CPU"] = "si pi yu",
            ["CSS"] = "si es es",
            ["CVE"] = "si vi i",
            ["GPU"] = "ci pi yu",
            ["HTML"] = "eyç ti em el",
            ["HTTP"] = "eyç ti ti pi",
            ["HTTPS"] = "eyç ti ti pi es",
            ["IDE"] = "ay di i",
            ["JSON"] = "ceyson",
            ["JWT"] = "ce dabılyu ti",
            ["LLM"] = "el el em",
            ["MCP"] = "em si pi",
            ["ORM"] = "o ar em",
            ["RAG"] = "rag",
            ["RAM"] = "ram",
            ["REST"] = "rest",
            ["SDK"] = "es di key",
            ["SQL"] = "es kü el",
            ["SSH"] = "es es eyç",
            ["SSL"] = "es es el",
            ["TLS"] = "ti el es",
            ["UI"] = "yu ay",
            ["URL"] = "yu ar el",
            ["UX"] = "yu eks",
            ["VPN"] = "vi pi en",
            ["XSS"] = "eks es es",
            ["YAML"] = "yamıl"
        };

    /// <summary>
    /// Units, written after a number. Read as letters they are noise; read as
    /// words they are information.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> Units =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["KB"] = "kilobayt",
            ["MB"] = "megabayt",
            ["GB"] = "gigabayt",
            ["TB"] = "terabayt",
            ["PB"] = "petabayt",
            ["Hz"] = "hertz",
            ["kHz"] = "kilohertz",
            ["MHz"] = "megahertz",
            ["GHz"] = "gigahertz",
            ["ms"] = "milisaniye",
            ["sn"] = "saniye",
            ["dk"] = "dakika",
            ["km"] = "kilometre",
            ["kW"] = "kilovat",
            ["MW"] = "megavat",
            ["GW"] = "gigavat"
        };

    /// <summary>
    /// Turkish abbreviations that end in a period without ending a sentence.
    /// Stored without the trailing dot.
    /// </summary>
    public static readonly IReadOnlySet<string> Abbreviations = new HashSet<string>(StringComparer.Ordinal)
    {
        "Dr", "Doç", "Prof", "Av", "Sn", "Bkz", "bkz", "Örn", "örn",
        "vb", "vs", "yy", "bkz", "Mah", "Cad", "Sok", "Apt", "No", "no",
        "Ltd", "Şti", "A.Ş", "T.C", "M.Ö", "M.S", "Alb", "Yzb", "Gen",
        "Hz", "Bşk", "Müh", "Tel", "Fak", "Ör", "yak", "min", "maks", "ort"
    };

    /// <summary>Expands a Turkish percentage, where the sign precedes the number.</summary>
    [GeneratedRegex(@"%\s*(?<value>\d+(?:[.,]\d+)?)")]
    public static partial Regex Percentage();

    /// <summary>"20M$", "1,5B$", "300K€" — magnitude suffix plus a currency sign.</summary>
    [GeneratedRegex(@"(?<value>\d+(?:[.,]\d+)?)\s*(?<scale>[KMB])?\s*(?<currency>[$€₺£])")]
    public static partial Regex ScaledCurrency();

    /// <summary>"$20", "€1,5" — sign first, as English sources write it.</summary>
    [GeneratedRegex(@"(?<currency>[$€₺£])\s*(?<value>\d+(?:[.,]\d+)?)\s*(?<scale>[KMB])?")]
    public static partial Regex LeadingCurrency();

    public static string CurrencyName(char sign) => sign switch
    {
        '$' => "dolar",
        '€' => "euro",
        '₺' => "lira",
        '£' => "sterlin",
        _ => string.Empty
    };

    public static string ScaleName(string? scale) => scale switch
    {
        "K" => "bin",
        "M" => "milyon",
        "B" => "milyar",
        _ => string.Empty
    };
}
