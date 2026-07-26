using FocusAI.Domain.Entities.Content;

namespace FocusAI.Application.Common.Interfaces;

/// <summary>Ambient clock, injected so time-dependent logic stays testable.</summary>
public interface IDateTimeProvider
{
    DateTimeOffset UtcNow { get; }

    DateOnly TodayIn(string timeZoneId);

    /// <summary>Converts a UTC instant into the user's wall clock.</summary>
    DateTimeOffset ToLocal(DateTimeOffset utc, string timeZoneId);
}

/// <summary>Identity of the caller behind the current request.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }

    string? Email { get; }

    bool IsAuthenticated { get; }

    bool IsInRole(string role);
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string hash);
}

public sealed record AuthTokens(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    DateTimeOffset RefreshTokenExpiresAt);

public interface ITokenService
{
    AuthTokens Issue(Guid userId, string email, IEnumerable<string> roles);

    /// <summary>Stable hash used to store refresh tokens without keeping the secret.</summary>
    string HashRefreshToken(string refreshToken);
}

public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default);

    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default);

    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
}

/// <summary>One raw item as returned by a source adapter, before normalisation.</summary>
public sealed record FeedItem
{
    public required string ExternalId { get; init; }

    public required string Url { get; init; }

    public required string Title { get; init; }

    public string? Author { get; init; }

    public string? Excerpt { get; init; }

    public string? Content { get; init; }

    public string? ImageUrl { get; init; }

    public DateTimeOffset? PublishedAt { get; init; }

    public int? EngagementScore { get; init; }

    public int? CommentCount { get; init; }
}

public sealed record FeedFetchResult
{
    public required IReadOnlyList<FeedItem> Items { get; init; }

    public string? ETag { get; init; }

    public string? LastModified { get; init; }

    /// <summary>True when the server answered 304 — nothing changed, nothing to do.</summary>
    public bool NotModified { get; init; }

    public bool Succeeded { get; init; } = true;

    public string? Error { get; init; }

    public static FeedFetchResult Empty(bool notModified = false) =>
        new() { Items = [], NotModified = notModified };

    public static FeedFetchResult Failure(string error) =>
        new() { Items = [], Succeeded = false, Error = error };
}

/// <summary>One adapter per <see cref="Domain.Enums.SourceKind"/>.</summary>
public interface IFeedAdapter
{
    bool CanHandle(Source source);

    Task<FeedFetchResult> FetchAsync(Source source, CancellationToken cancellationToken = default);
}

public interface IFeedAdapterResolver
{
    IFeedAdapter Resolve(Source source);
}

/// <summary>
/// What a single page fetch yields: the readable body text and the best lead
/// image we could find (og:image / twitter:image / a significant inline image).
/// Either may be null — extraction is opportunistic and never fails the item.
/// </summary>
public sealed record ExtractedArticle(string? Text, string? ImageUrl)
{
    public static readonly ExtractedArticle Empty = new(null, null);
}

/// <summary>Pulls readable body text and a lead image out of an article page.</summary>
public interface IContentExtractor
{
    Task<ExtractedArticle> ExtractAsync(string url, CancellationToken cancellationToken = default);
}

public sealed record SearchHit(Guid StoryId, double Score);

/// <summary>External search index (Meilisearch/Typesense per PRD section 12).</summary>
public interface ISearchIndex
{
    bool IsEnabled { get; }

    Task IndexAsync(IReadOnlyList<Story> stories, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid storyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        int take,
        CancellationToken cancellationToken = default);
}

/// <summary>Semantic lookup over the pgvector column.</summary>
public interface IVectorSearch
{
    Task<IReadOnlyList<SearchHit>> FindSimilarStoriesAsync(
        float[] embedding,
        int take,
        Guid? excludeStoryId = null,
        CancellationToken cancellationToken = default);
}

public sealed record PushMessage(string Title, string Body, string Url, string? Tag = null);

public interface IPushNotifier
{
    Task SendAsync(
        Domain.Entities.Users.PushSubscription subscription,
        PushMessage message,
        CancellationToken cancellationToken = default);
}
