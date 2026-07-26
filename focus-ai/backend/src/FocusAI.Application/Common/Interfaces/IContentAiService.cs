using FocusAI.Domain.Enums;

namespace FocusAI.Application.Common.Interfaces;

/// <summary>What the summariser is given about a story cluster.</summary>
public sealed record StoryPromptContext
{
    public required string Title { get; init; }

    public required IReadOnlyList<string> ArticleExcerpts { get; init; }

    public required IReadOnlyList<string> SourceNames { get; init; }

    public string Language { get; init; } = "tr";

    public DateTimeOffset PublishedAt { get; init; }
}

/// <summary>Structured five-part summary from PRD section 5.2.</summary>
public sealed record StorySummaryResult
{
    public required string Title { get; init; }

    public required string Summary { get; init; }

    public string? Dek { get; init; }

    public string? WhyItMatters { get; init; }

    public string? WhoIsAffected { get; init; }

    public string? WhatShouldIDo { get; init; }

    public IReadOnlyList<string> KeyPoints { get; init; } = [];

    public IReadOnlyList<string> TopicSlugs { get; init; } = [];

    public ContentCategory Category { get; init; } = ContentCategory.Unknown;

    /// <summary>[0,1] editorial significance, feeds the importance score.</summary>
    public double Importance { get; init; } = 0.5;

    /// <summary>[0,1] technical specificity, feeds the trust score.</summary>
    public double TechnicalAccuracy { get; init; } = 0.6;

    public int ReadingMinutes { get; init; } = 1;

    public string Provider { get; init; } = "none";

    public string Model { get; init; } = "none";

    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }
}

/// <summary>Opinionated commentary from PRD section 5.5.</summary>
public sealed record StoryAnalysisResult
{
    public required string WhyImportant { get; init; }

    public string? RealImpact { get; init; }

    public HypeLevel Hype { get; init; } = HypeLevel.Accurate;

    public string? HypeReasoning { get; init; }

    public LearnUrgency LearnUrgency { get; init; } = LearnUrgency.Watch;

    public LongevityOutlook Longevity { get; init; } = LongevityOutlook.Uncertain;

    public string? LongevityReasoning { get; init; }

    public IReadOnlyDictionary<string, string> StackNotes { get; init; } = new Dictionary<string, string>();

    public double Confidence { get; init; } = 0.5;

    public string Provider { get; init; } = "none";

    public string Model { get; init; } = "none";
}

/// <summary>
/// Structured filter parsed out of a natural-language query (PRD section 9),
/// e.g. "Son bir ayda çıkan tüm AI Agent haberlerini göster."
/// </summary>
public sealed record ParsedSearchQuery
{
    /// <summary>Keyword portion handed to the full-text/vector index.</summary>
    public string Text { get; init; } = string.Empty;

    public IReadOnlyList<string> TopicSlugs { get; init; } = [];

    public ContentCategory? Category { get; init; }

    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public int? MinTrustScore { get; init; }

    public bool OfficialSourcesOnly { get; init; }

    /// <summary>True when an LLM produced the parse; false for the regex fallback.</summary>
    public bool ParsedByLlm { get; init; }
}

public sealed record LearningSuggestionResult
{
    public required string Title { get; init; }

    public required string Rationale { get; init; }

    public string? TopicSlug { get; init; }

    public int EstimatedMinutes { get; init; } = 15;

    public IReadOnlyList<LearningResourceResult> Resources { get; init; } = [];
}

public sealed record LearningResourceResult(string Title, string Url, string Kind, int EstimatedMinutes);

public sealed record AskAnswer
{
    public required string Answer { get; init; }

    public IReadOnlyList<Guid> CitedStoryIds { get; init; } = [];

    public double Confidence { get; init; } = 0.5;
}

/// <summary>
/// High-level AI operations the Application layer needs. Keeping prompts behind
/// this port means a handler never has to know what a system prompt looks like.
/// </summary>
public interface IContentAiService
{
    Task<StorySummaryResult> SummarizeAsync(
        StoryPromptContext context,
        CancellationToken cancellationToken = default);

    Task<StoryAnalysisResult> AnalyzeAsync(
        StoryPromptContext context,
        CancellationToken cancellationToken = default);

    Task<ParsedSearchQuery> ParseSearchQueryAsync(
        string rawQuery,
        DateTimeOffset now,
        CancellationToken cancellationToken = default);

    Task<string> WriteDigestIntroAsync(
        IReadOnlyList<string> headlines,
        string language,
        CancellationToken cancellationToken = default);

    Task<LearningSuggestionResult?> SuggestLearningAsync(
        IReadOnlyList<string> interestSlugs,
        IReadOnlyList<string> recentHeadlines,
        int minutes,
        string language,
        CancellationToken cancellationToken = default);

    Task<AskAnswer> AnswerAsync(
        string question,
        IReadOnlyList<(Guid StoryId, string Title, string Summary)> context,
        string language,
        CancellationToken cancellationToken = default);
}
