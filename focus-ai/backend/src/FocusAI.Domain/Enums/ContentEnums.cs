namespace FocusAI.Domain.Enums;

/// <summary>How a source is polled. Drives which ingestion adapter runs.</summary>
public enum SourceKind
{
    Rss = 0,
    Atom = 1,
    JsonFeed = 2,
    HackerNews = 3,
    Reddit = 4,
    GitHubTrending = 5,
    GitHubReleases = 6,
    Arxiv = 7,
    YouTubeRss = 8,
    ProductHunt = 9,
    PapersWithCode = 10
}

/// <summary>Buckets from PRD section 6 — used for source-mix guarantees in the digest.</summary>
public enum SourceCategory
{
    OfficialBlog = 0,
    Community = 1,
    Code = 2,
    Academic = 3,
    Video = 4,
    News = 5
}

/// <summary>Topical bucket a story lands in. Maps to the home-screen cards (PRD section 7).</summary>
public enum ContentCategory
{
    Unknown = 0,
    Ai = 1,
    Software = 2,
    OpenSource = 3,
    Startup = 4,
    Science = 5,
    Career = 6,
    Tools = 7,
    Security = 8,
    Hardware = 9,
    Product = 10
}

public enum ArticleStatus
{
    Fetched = 0,
    Normalized = 1,
    Embedded = 2,
    Clustered = 3,
    Skipped = 4,
    Failed = 5
}

public enum StoryStatus
{
    /// <summary>Cluster exists but has no AI summary yet.</summary>
    Draft = 0,
    /// <summary>Summary/analysis generation in flight.</summary>
    Enriching = 1,
    Published = 2,
    Archived = 3,
    /// <summary>Held back by a moderator or by low-quality heuristics.</summary>
    Suppressed = 4
}

/// <summary>Extra material attached to a story detail page (PRD section 8).</summary>
public enum StoryLinkKind
{
    Article = 0,
    Video = 1,
    GitHub = 2,
    Paper = 3,
    Discussion = 4,
    Documentation = 5
}
