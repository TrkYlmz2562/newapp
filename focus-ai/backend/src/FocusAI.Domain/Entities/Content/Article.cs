using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// One raw item pulled from one source. Twenty articles about the same launch
/// collapse into a single <see cref="Story"/>; the articles themselves are kept
/// so the detail page can list "Kaynaklar" (PRD section 5.3).
/// </summary>
public class Article : AuditableEntity
{
    public Guid SourceId { get; set; }

    public Source? Source { get; set; }

    /// <summary>Feed-provided identity (guid/id element), used for idempotent re-ingest.</summary>
    public required string ExternalId { get; set; }

    public required string Url { get; set; }

    /// <summary>URL after tracking params are stripped and redirects resolved.</summary>
    public required string CanonicalUrl { get; set; }

    public required string Title { get; set; }

    public string? Author { get; set; }

    /// <summary>Feed-provided description, before any AI touches it.</summary>
    public string? Excerpt { get; set; }

    /// <summary>Extracted readable body text, when the crawler could get one.</summary>
    public string? Content { get; set; }

    public string? ImageUrl { get; set; }

    /// <summary>
    /// When the one-off image backfill last fetched this page, whether or not it
    /// found anything. Without it a page with genuinely no image is re-fetched on
    /// every run, so the job never finishes and keeps hammering the publisher.
    /// </summary>
    public DateTimeOffset? ImageCheckedAt { get; set; }

    public string Language { get; set; } = "en";

    public DateTimeOffset PublishedAt { get; set; }

    public DateTimeOffset FetchedAt { get; set; }

    public ArticleStatus Status { get; set; } = ArticleStatus.Fetched;

    /// <summary>SHA-256 of the normalized title+body. Catches byte-identical reposts.</summary>
    public required string ContentHash { get; set; }

    /// <summary>
    /// 64-bit SimHash of the normalized text. Cheap Hamming-distance prefilter
    /// before the expensive embedding comparison.
    /// </summary>
    public long SimHash { get; set; }

    /// <summary>Populated by the embedding stage; column type is pgvector.</summary>
    public float[]? Embedding { get; set; }

    /// <summary>Engagement metric where the source exposes one (HN points, Reddit score, GitHub stars).</summary>
    public int? EngagementScore { get; set; }

    public int? CommentCount { get; set; }

    public Guid? StoryId { get; set; }

    public Story? Story { get; set; }

    /// <summary>True for the article chosen to represent its cluster.</summary>
    public bool IsPrimary { get; set; }

    public string? FailureReason { get; set; }

    /// <summary>Best available text for embedding/summarisation, longest first.</summary>
    public string BestText()
    {
        if (!string.IsNullOrWhiteSpace(Content))
        {
            return Content;
        }

        return !string.IsNullOrWhiteSpace(Excerpt) ? Excerpt : Title;
    }
}
