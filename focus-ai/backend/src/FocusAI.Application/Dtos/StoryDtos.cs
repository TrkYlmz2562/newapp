using FocusAI.Domain.Enums;

namespace FocusAI.Application.Dtos;

public sealed record TopicDto(Guid Id, string Name, string Slug, TopicKind Kind);

public sealed record SourceRefDto(
    Guid Id,
    string Name,
    string Slug,
    string Url,
    bool IsOfficial,
    string? IconUrl,
    DateTimeOffset PublishedAt,
    string ArticleTitle);

public sealed record TrustDto(
    int Total,
    int OfficialSourceScore,
    int CorroborationScore,
    int RecencyScore,
    int TechnicalAccuracyScore,
    int CommunityScore,
    string? Explanation);

public sealed record AnalysisDto(
    string WhyImportant,
    string? RealImpact,
    HypeLevel Hype,
    string? HypeReasoning,
    LearnUrgency LearnUrgency,
    LongevityOutlook Longevity,
    string? LongevityReasoning,
    IReadOnlyDictionary<string, string> StackNotes,
    double Confidence);

public sealed record StoryLinkDto(StoryLinkKind Kind, string Url, string Title, string? Description, string? ThumbnailUrl);

/// <summary>Compact shape used by feed lists, digests and search results.</summary>
public sealed record StoryCardDto
{
    public required Guid Id { get; init; }

    public required string Slug { get; init; }

    public required string Title { get; init; }

    public string? Dek { get; init; }

    public required ContentCategory Category { get; init; }

    public string? Summary { get; init; }

    public string? WhyItMatters { get; init; }

    public string? HeroImageUrl { get; init; }

    /// <summary>Subject the card sets in display type when there is no photo.</summary>
    public string? VisualEntity { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public required int TrustScore { get; init; }

    public required int ImportanceScore { get; init; }

    public required int SourceCount { get; init; }

    public required int ReadingMinutes { get; init; }

    public IReadOnlyList<TopicDto> Topics { get; init; } = [];

    public bool IsBookmarked { get; init; }

    /// <summary>
    /// True when this reader has already opened the story. The ranker demotes
    /// seen stories; showing it is what stops that looking arbitrary.
    /// </summary>
    public bool IsRead { get; init; }

    /// <summary>
    /// This reader's own verdict, when they gave one: <c>Helpful</c> or
    /// <c>NotHelpful</c>. Null means they have not said. Sent so the buttons
    /// still show the choice after a reload.
    /// </summary>
    public InteractionType? Feedback { get; init; }

    /// <summary>Why this card is in front of this reader — filled by ranked endpoints only.</summary>
    public string? Reason { get; init; }
}

/// <summary>Everything the detail page in PRD section 8 renders.</summary>
public sealed record StoryDetailDto
{
    public required Guid Id { get; init; }

    public required string Slug { get; init; }

    public required string Title { get; init; }

    public string? Dek { get; init; }

    public required ContentCategory Category { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public required DateTimeOffset LastActivityAt { get; init; }

    public string? HeroImageUrl { get; init; }

    /// <summary>Subject the hero sets in display type when there is no photo.</summary>
    public string? VisualEntity { get; init; }

    public required int ReadingMinutes { get; init; }

    public required int ImportanceScore { get; init; }

    public string? Summary { get; init; }

    public string? WhyItMatters { get; init; }

    public string? WhoIsAffected { get; init; }

    public string? WhatShouldIDo { get; init; }

    public IReadOnlyList<string> KeyPoints { get; init; } = [];

    public string? ExtendedSummary { get; init; }

    public TrustDto? Trust { get; init; }

    public AnalysisDto? Analysis { get; init; }

    public IReadOnlyList<SourceRefDto> Sources { get; init; } = [];

    public IReadOnlyList<StoryLinkDto> Links { get; init; } = [];

    /// <summary>How the sources covering this story agree and differ. Empty with one source.</summary>
    public IReadOnlyList<ComparisonPointDto> Comparison { get; init; } = [];

    public IReadOnlyList<TopicDto> Topics { get; init; } = [];

    public IReadOnlyList<StoryCardDto> Related { get; init; } = [];

    public bool IsBookmarked { get; init; }

    /// <summary>This reader's own verdict, when they gave one. See <see cref="StoryCardDto.Feedback"/>.</summary>
    public InteractionType? Feedback { get; init; }

    /// <summary>
    /// Stack-specific note picked out of <see cref="AnalysisDto.StackNotes"/> for
    /// this reader's declared interests — the ".NET geliştiriyorsan…" callout.
    /// </summary>
    public string? PersonalNote { get; init; }
}

/// <summary>
/// One observation about how the outlets covered a story. The quote and the
/// attributed outlet travel with it so the reader can check the claim rather than
/// take the comparison on trust.
/// </summary>
public sealed record ComparisonPointDto(
    string Text,
    ComparisonKind Kind,
    IReadOnlyList<string> Sources,
    string Quote,
    string QuoteSource);

/// <summary>
/// One committed finance development. There is deliberately no score, percentage
/// or rating here: the card shows what makes the item checkable — the tier's name,
/// the document behind it, and a verbatim sentence — rather than a number implying
/// a likelihood the product cannot know.
/// </summary>
public sealed record FinanceItemDto(
    Guid Id,
    string Slug,
    string Title,
    CommitmentTier Tier,
    EventHorizon Horizon,
    FinanceInstrument Instrument,
    string? Event,
    string? DateText,
    DateOnly? EventDate,
    DatePrecision DatePrecision,
    string? Quote,
    string? Condition,
    string? Reference,
    int SourceCount,
    DateTimeOffset PublishedAt,
    IReadOnlyList<string> Topics);

public sealed record FinanceFeedDto(
    IReadOnlyList<FinanceItemDto> Realized,
    IReadOnlyList<FinanceItemDto> Soon,
    IReadOnlyList<FinanceItemDto> Later,
    DateTimeOffset GeneratedAt);
