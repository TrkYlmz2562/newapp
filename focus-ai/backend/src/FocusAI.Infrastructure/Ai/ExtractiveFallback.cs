using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Text;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Deterministic, non-generative summaries used when no LLM is configured or a
/// call fails.
/// </summary>
/// <remarks>
/// This is what makes the pipeline honest under failure: rather than publishing
/// nothing, or publishing invented analysis, the story ships with genuine
/// extracted sentences and a visibly neutral assessment. It also means the whole
/// system is runnable end-to-end in development with zero API keys.
/// </remarks>
internal static class ExtractiveFallback
{
    private static readonly char[] SentenceEnders = ['.', '!', '?', '\n'];

    private static readonly Dictionary<string, ContentCategory> CategoryKeywords = new(StringComparer.Ordinal)
    {
        ["llm"] = ContentCategory.Ai,
        ["gpt"] = ContentCategory.Ai,
        ["model"] = ContentCategory.Ai,
        ["agent"] = ContentCategory.Ai,
        ["transformer"] = ContentCategory.Ai,
        ["open source"] = ContentCategory.OpenSource,
        ["release"] = ContentCategory.Software,
        ["version"] = ContentCategory.Software,
        ["framework"] = ContentCategory.Software,
        ["vulnerability"] = ContentCategory.Security,
        ["cve"] = ContentCategory.Security,
        ["security"] = ContentCategory.Security,
        ["funding"] = ContentCategory.Startup,
        ["startup"] = ContentCategory.Startup,
        ["hiring"] = ContentCategory.Career,
        ["paper"] = ContentCategory.Science,
        ["arxiv"] = ContentCategory.Science,
        ["gpu"] = ContentCategory.Hardware,
        ["chip"] = ContentCategory.Hardware
    };

    public static StorySummaryResult Summarize(StoryPromptContext context)
    {
        var body = context.ArticleExcerpts.FirstOrDefault() ?? context.Title;
        var sentences = SplitSentences(body);

        var summary = sentences.Count > 0
            ? string.Join(' ', sentences.Take(3))
            : context.Title;

        var sourceList = context.SourceNames.Count > 0
            ? string.Join(", ", context.SourceNames.Take(4))
            : "tek kaynak";

        return new StorySummaryResult
        {
            Title = context.Title,
            Dek = sentences.FirstOrDefault(),
            Summary = FieldLimits.Cap(summary, 1200)!,
            WhyItMatters = null,
            WhoIsAffected = null,
            // Deliberately not an invented recommendation — this path has no analysis.
            WhatShouldIDo = null,
            // Word-boundary aware: a hard cut mid-token ("…absorb the co") reads
            // as corrupted data rather than as an excerpt.
            KeyPoints = sentences.Skip(1).Take(3)
                .Select(sentence => FieldLimits.Cap(sentence, 240)!)
                .ToList(),
            TopicSlugs = [],
            Category = GuessCategory($"{context.Title} {body}"),
            Importance = EstimateImportance(context),
            TechnicalAccuracy = 0.5,
            ReadingMinutes = EstimateReadingMinutes(body),
            Provider = "extractive-fallback",
            Model = $"kaynaklar: {sourceList}"
        };
    }

    /// <summary>
    /// No commentary is generated without a model. The single factual statement
    /// returned here is derived from the cluster itself, and the low confidence
    /// keeps the UI from presenting it as analysis.
    /// </summary>
    public static StoryAnalysisResult Analyze(StoryPromptContext context)
    {
        var sourceCount = context.SourceNames.Count;

        var why = sourceCount > 1
            ? $"Bu gelişme {sourceCount} farklı kaynakta yer aldı."
            : "Bu gelişme şu an tek kaynakta yer alıyor.";

        return new StoryAnalysisResult
        {
            WhyImportant = why,
            RealImpact = null,
            Hype = HypeLevel.Accurate,
            HypeReasoning = null,
            LearnUrgency = LearnUrgency.Watch,
            Longevity = LongevityOutlook.Uncertain,
            LongevityReasoning = null,
            StackNotes = new Dictionary<string, string>(),
            // Below the threshold the UI uses to show the analysis block at all.
            Confidence = 0.2,
            Provider = "extractive-fallback",
            Model = "none"
        };
    }

    /// <summary>
    /// Phrases that carry a time constraint, longest first so "son bir ay" is
    /// matched before "son". Each maps to a lookback in days.
    /// </summary>
    private static readonly (string Phrase, int Days)[] TimePhrases =
    [
        ("son uc ayda", 90), ("son uc ay", 90), ("son 3 ayda", 90), ("son 3 ay", 90),
        ("last three months", 90), ("last 3 months", 90),
        ("son bir ayda", 30), ("son bir ay", 30), ("gecen ay", 30), ("son ayda", 30),
        ("son ay", 30), ("bu ayda", 30), ("bu ay", 30),
        ("past month", 30), ("last month", 30), ("this month", 30),
        ("son bir haftada", 7), ("son bir hafta", 7), ("gecen hafta", 7),
        ("bu haftada", 7), ("bu hafta", 7), ("son hafta", 7),
        ("past week", 7), ("last week", 7), ("this week", 7),
        ("bugun", 1), ("today", 1),
        ("bu yilda", 365), ("bu yil", 365), ("son yil", 365), ("this year", 365)
    ];

    private static readonly string[] FillerPhrases =
    [
        "haberlerini goster", "haberleri goster", "haberlerini", "haberleri", "haber",
        "cikan", "yayinlanan", "olan", "goster", "listele", "bul", "getir", "tum",
        "show me", "show all", "find all", "list all", "news about", "news", "all"
    ];

    /// <summary>
    /// Regex-free relative-date parsing for the common Turkish and English
    /// phrasings, so search still narrows by time with no model available.
    /// </summary>
    public static ParsedSearchQuery ParseQuery(string rawQuery, DateTimeOffset now)
    {
        var normalized = TextNormalizer.Normalize(rawQuery);
        DateTimeOffset? from = null;
        var text = normalized;

        foreach (var (phrase, days) in TimePhrases)
        {
            var needle = TextNormalizer.Normalize(phrase);
            if (!text.Contains(needle, StringComparison.Ordinal))
            {
                continue;
            }

            from = now.AddDays(-days);

            // Strip the phrase so it does not end up as a keyword. Leaving
            // "son bir ayda" in the search text is what made the whole query
            // match nothing at all.
            text = text.Replace(needle, " ", StringComparison.Ordinal);
            break;
        }

        var officialOnly = Contains(normalized, "resmi kaynak", "official source");
        if (officialOnly)
        {
            text = text
                .Replace(TextNormalizer.Normalize("resmi kaynak"), " ", StringComparison.Ordinal)
                .Replace(TextNormalizer.Normalize("official source"), " ", StringComparison.Ordinal);
        }

        foreach (var filler in FillerPhrases)
        {
            text = text.Replace(TextNormalizer.Normalize(filler), " ", StringComparison.Ordinal);
        }

        text = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return new ParsedSearchQuery
        {
            // If stripping removed everything, fall back to the original query
            // rather than searching for nothing.
            Text = text.Length > 0 ? text : normalized,
            TopicSlugs = [],
            Category = null,
            From = from,
            To = null,
            MinTrustScore = null,
            OfficialSourcesOnly = officialOnly,
            ParsedByLlm = false
        };
    }

    public static string DigestIntro(IReadOnlyList<string> headlines) =>
        headlines.Count switch
        {
            0 => "Bugün öne çıkan bir gelişme yok.",
            1 => "Bugün tek bir gelişme öne çıkıyor.",
            _ => $"Bugün için {headlines.Count} gelişme seçildi."
        };

    private static List<string> SplitSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return text
            .Split(SentenceEnders, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(s => s.Length > 25)
            .Select(s => s.EndsWith('.') ? s : s + ".")
            .ToList();
    }

    private static ContentCategory GuessCategory(string text)
    {
        var normalized = TextNormalizer.Normalize(text);

        foreach (var (keyword, category) in CategoryKeywords)
        {
            if (normalized.Contains(keyword, StringComparison.Ordinal))
            {
                return category;
            }
        }

        return ContentCategory.Software;
    }

    /// <summary>
    /// Corroboration is the only signal available without a model, so importance
    /// is a plain function of how many outlets carried the story.
    /// </summary>
    private static double EstimateImportance(StoryPromptContext context) =>
        Math.Clamp(0.3 + context.SourceNames.Count * 0.1, 0.3, 0.8);

    private static int EstimateReadingMinutes(string text)
    {
        // ~200 words per minute is the usual reading-speed assumption.
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        return Math.Clamp((int)Math.Ceiling(words / 200d), 1, 10);
    }

    private static bool Contains(string haystack, params string[] needles) =>
        needles.Any(needle => haystack.Contains(TextNormalizer.Normalize(needle), StringComparison.Ordinal));

}
