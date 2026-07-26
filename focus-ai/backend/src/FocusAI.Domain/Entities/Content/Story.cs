using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// The unit the user actually reads: one real-world development, merged from
/// every article that covered it (PRD section 5.3).
/// </summary>
public class Story : AuditableEntity
{
    public required string Slug { get; set; }

    /// <summary>Neutral, de-clickbaited headline written by the summariser.</summary>
    public required string Title { get; set; }

    /// <summary>One-line standfirst shown on cards.</summary>
    public string? Dek { get; set; }

    public ContentCategory Category { get; set; } = ContentCategory.Unknown;

    public StoryStatus Status { get; set; } = StoryStatus.Draft;

    /// <summary>
    /// When this story first became visible to readers, or null if it never has.
    /// </summary>
    /// <remarks>
    /// <see cref="Status"/> cannot answer "has anyone been able to link to this
    /// yet": clustering demotes a Published story back to Enriching every time new
    /// coverage arrives, so a story that has been live for days reads as unpublished
    /// for the duration of its re-enrichment. Anything that must not change after
    /// first publication — the permalink above all — has to key off this instead.
    /// Set once and never cleared.
    /// </remarks>
    public DateTimeOffset? FirstPublishedAt { get; set; }

    /// <summary>Earliest publish time across the cluster — when the news actually broke.</summary>
    public DateTimeOffset PublishedAt { get; set; }

    /// <summary>Latest publish time across the cluster — bumped as coverage keeps arriving.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    public Guid? PrimaryArticleId { get; set; }

    public string? HeroImageUrl { get; set; }

    /// <summary>Centroid of member article embeddings; drives related-story lookup and search.</summary>
    public float[]? Embedding { get; set; }

    /// <summary>Denormalised for cheap ordering and for the "N kaynak doğruladı" badge.</summary>
    public int SourceCount { get; set; }

    public int OfficialSourceCount { get; set; }

    /// <summary>
    /// True when the story's own text reports the claim rather than asserting it —
    /// the Turkish -mış evidential, "iddia edildi", "öğrenildi".
    /// </summary>
    /// <remarks>
    /// Set deterministically from the text, with no model involved, which is the
    /// whole point: it is the one hearsay signal that still works when no LLM is
    /// configured. Corroboration alone cannot replace it — three outlets repeating
    /// the same rumour corroborate the rumour, not the fact — so a finance story
    /// that reaches the feed on source count alone still has to clear this.
    ///
    /// Only meaningful on Turkish text; an English source carries no -mış, so the
    /// check passes trivially there and corroboration does the work instead.
    /// </remarks>
    public bool HasEvidentialClaim { get; set; }

    /// <summary>0-100 confidence rating (PRD section 5.4).</summary>
    public int TrustScore { get; set; }

    /// <summary>0-100 editorial weight used to rank the daily digest.</summary>
    public int ImportanceScore { get; set; }

    /// <summary>Summed engagement across member articles (HN points, Reddit score…).</summary>
    public int EngagementScore { get; set; }

    /// <summary>Estimated reading time of the AI summary, in minutes.</summary>
    public int ReadingMinutes { get; set; } = 1;

    public ICollection<Article> Articles { get; set; } = [];

    /// <summary>Finance-only: how firmly this development is committed. Null elsewhere.</summary>
    public StoryCommitment? Commitment { get; set; }

    /// <summary>How the sources covering this story agree and differ. Needs 2+ sources.</summary>
    public StoryComparison? Comparison { get; set; }

    public ICollection<StoryTopic> Topics { get; set; } = [];

    public ICollection<StoryLink> Links { get; set; } = [];

    public StorySummary? Summary { get; set; }

    public StoryAnalysis? Analysis { get; set; }

    public StoryTrust? Trust { get; set; }

    /// <summary>
    /// Recompute the denormalised counters after the cluster membership changes.
    /// Callers pass the member articles because the domain layer never queries.
    /// </summary>
    public void RefreshAggregates(IReadOnlyCollection<Article> members, IReadOnlyCollection<Source> sources)
    {
        if (members.Count == 0)
        {
            return;
        }

        var officialIds = sources.Where(s => s.IsOfficial).Select(s => s.Id).ToHashSet();

        SourceCount = members.Select(a => a.SourceId).Distinct().Count();
        OfficialSourceCount = members.Select(a => a.SourceId).Distinct().Count(officialIds.Contains);
        EngagementScore = members.Sum(a => a.EngagementScore ?? 0);
        PublishedAt = members.Min(a => a.PublishedAt);
        LastActivityAt = members.Max(a => a.PublishedAt);
    }
}
