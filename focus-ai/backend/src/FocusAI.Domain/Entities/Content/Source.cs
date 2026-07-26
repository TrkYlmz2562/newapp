using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// A pollable content origin — one row per entry in PRD section 6.
/// </summary>
public class Source : AuditableEntity
{
    public required string Name { get; set; }

    public required string Slug { get; set; }

    /// <summary>Human-facing home page.</summary>
    public required string WebsiteUrl { get; set; }

    /// <summary>Feed/API endpoint the ingestion adapter polls.</summary>
    public required string FeedUrl { get; set; }

    public SourceKind Kind { get; set; } = SourceKind.Rss;

    public SourceCategory Category { get; set; } = SourceCategory.Community;

    /// <summary>Default topical bucket for items with no better classification signal.</summary>
    public ContentCategory DefaultContentCategory { get; set; } = ContentCategory.Unknown;

    /// <summary>
    /// True for first-party vendor blogs (OpenAI, Anthropic, GitHub…). Feeds the
    /// "Resmi kaynak" component of the trust score.
    /// </summary>
    public bool IsOfficial { get; set; }

    /// <summary>
    /// Editorial reliability multiplier in [0,1]. A 0.95 source corroborating a
    /// claim moves the trust score far more than a 0.4 aggregator.
    /// </summary>
    public double TrustWeight { get; set; } = 0.6;

    public string Language { get; set; } = "en";

    public bool IsEnabled { get; set; } = true;

    public int FetchIntervalMinutes { get; set; } = 30;

    public DateTimeOffset? LastFetchedAt { get; set; }

    public DateTimeOffset? LastSucceededAt { get; set; }

    /// <summary>Consecutive failures; the scheduler backs off exponentially on this.</summary>
    public int ConsecutiveFailures { get; set; }

    public string? LastError { get; set; }

    /// <summary>HTTP caching validators so re-polls stay cheap.</summary>
    public string? ETag { get; set; }

    public string? LastModified { get; set; }

    public string? IconUrl { get; set; }

    public ICollection<Article> Articles { get; set; } = [];

    /// <summary>
    /// A source is due when it has never been fetched, or when its interval has
    /// elapsed. Failures push the next attempt out exponentially (capped at 24×)
    /// so a dead feed does not burn the crawler budget every cycle.
    /// </summary>
    public bool IsDue(DateTimeOffset now)
    {
        if (!IsEnabled)
        {
            return false;
        }

        if (LastFetchedAt is null)
        {
            return true;
        }

        var backoffFactor = Math.Min(Math.Pow(2, ConsecutiveFailures), 24);
        var interval = TimeSpan.FromMinutes(FetchIntervalMinutes * backoffFactor);
        return now - LastFetchedAt.Value >= interval;
    }

    public void MarkSuccess(DateTimeOffset now, string? etag, string? lastModified)
    {
        LastFetchedAt = now;
        LastSucceededAt = now;
        ConsecutiveFailures = 0;
        LastError = null;
        ETag = etag ?? ETag;
        LastModified = lastModified ?? LastModified;
    }

    public void MarkFailure(DateTimeOffset now, string error)
    {
        LastFetchedAt = now;
        ConsecutiveFailures++;
        LastError = error.Length > 1000 ? error[..1000] : error;
    }
}
