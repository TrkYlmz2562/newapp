using FocusAI.Domain.Enums;
using FocusAI.Domain.Text;
using FocusAI.Domain.Timeline;

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

/// <summary>
/// The commitment classification behind a finance story: what was decided, by
/// whom, when it takes effect, and the sentence in the source that says so.
/// </summary>
/// <remarks>
/// This travels with the story rather than living in a section of its own. A
/// finance development belongs in the feed alongside everything else; what it
/// needs is a label saying how firm it is, not a separate address.
///
/// There is deliberately no score or percentage here. A number beside a financial
/// claim reads as a precision this product does not have — the card shows the
/// tier's name, the document behind it and a verbatim quote, all of which the
/// reader can check.
/// </remarks>
public sealed record CommitmentDto(
    CommitmentTier Tier,
    EventHorizon Horizon,
    FinanceInstrument Instrument,
    string? Event,
    string? DateText,
    DateOnly? EventDate,
    DatePrecision DatePrecision,
    string? Quote,
    string? Condition,
    string? Reference)
{
    /// <summary>
    /// True when the full tier × horizon matrix clears this as settled news rather
    /// than something still in motion. Computed on read, never stored: the matrix
    /// is the one in <see cref="CommitmentLexicon"/> and must not be duplicated
    /// into a column that can go stale against it.
    /// </summary>
    public bool IsSettled => CommitmentLexicon.IsPublishable(Tier, Horizon, Instrument);

    /// <summary>True when a real agreement is still waiting on a named approval.</summary>
    public bool IsConditional => CommitmentLexicon.IsConditional(Tier, Horizon);
}

/// <summary>
/// The shape of a story over time: when it broke, when an official body confirmed
/// it, whether it is still moving.
/// </summary>
/// <remarks>
/// Derived entirely from stored timestamps — no model, no cost, identical on an
/// install with no API keys. Answers a different question from the coverage
/// comparison beside it: that one is what the outlets said, this one is when.
/// </remarks>
public sealed record TimelineDto(
    StoryPhase Phase,
    DateTimeOffset FirstAt,
    DateTimeOffset LatestAt,
    int OutletCount,
    double SpanHours,
    double LongestQuietHours,
    IReadOnlyList<TimelineMomentDto> Moments);

public sealed record TimelineMomentDto(TimelineMomentKind Kind, DateTimeOffset At, string SourceName);

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
    /// Set on finance stories only. Null everywhere else — it is what the card
    /// badges instead of showing a trust number next to a money claim.
    /// </summary>
    public CommitmentDto? Commitment { get; init; }

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

    /// <summary>The commitment classification, on finance stories only.</summary>
    public CommitmentDto? Commitment { get; init; }

    /// <summary>How the story developed. Null only when it has no member articles.</summary>
    public TimelineDto? Timeline { get; init; }

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
