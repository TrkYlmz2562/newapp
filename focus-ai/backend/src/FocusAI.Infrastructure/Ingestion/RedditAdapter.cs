using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FocusAI.Infrastructure.Ingestion;

/// <summary>
/// Reddit listings via the public <c>.json</c> endpoint. Like Hacker News, the
/// score and comment count matter as much as the link itself.
/// </summary>
public sealed class RedditAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<IngestionOptions> options,
    ILogger<RedditAdapter> logger) : IFeedAdapter
{
    private const int MinimumScore = 40;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IngestionOptions _options = options.Value;

    public bool CanHandle(Source source) => source.Kind == SourceKind.Reddit;

    public async Task<FeedFetchResult> FetchAsync(Source source, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpClientFactory.CreateClient(IngestionServiceNames.HttpClient);

            var url = source.FeedUrl.Contains(".json", StringComparison.OrdinalIgnoreCase)
                ? source.FeedUrl
                : $"{source.FeedUrl.TrimEnd('/')}/hot.json?limit={_options.MaxItemsPerFeed}";

            var listing = await http.GetFromJsonAsync<RedditListing>(url, JsonOptions, cancellationToken);

            var children = listing?.Data?.Children ?? [];

            var items = children
                .Select(child => child.Data)
                .Where(post => post is not null)
                .Where(post => !post!.Stickied && post.Score >= MinimumScore)
                .Where(post => !string.IsNullOrWhiteSpace(post!.Title))
                .Take(_options.MaxItemsPerFeed)
                .Select(post => new FeedItem
                {
                    ExternalId = post!.Id ?? post.Permalink ?? post.Title!,
                    // Self-posts point at Reddit; link posts at the destination.
                    Url = post.IsSelf || string.IsNullOrWhiteSpace(post.Url)
                        ? $"https://www.reddit.com{post.Permalink}"
                        : post.Url,
                    Title = post.Title!,
                    Author = post.Author,
                    Excerpt = HtmlText.ToPlainText(post.Selftext),
                    ImageUrl = IsUsableThumbnail(post.Thumbnail) ? post.Thumbnail : null,
                    PublishedAt = DateTimeOffset.FromUnixTimeSeconds((long)post.CreatedUtc),
                    EngagementScore = post.Score,
                    CommentCount = post.NumComments
                })
                .ToList();

            return new FeedFetchResult { Items = items };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI Reddit fetch failed for {SourceSlug}", source.Slug);
            return FeedFetchResult.Failure(ex.Message);
        }
    }

    /// <summary>Reddit uses sentinel words like "self" and "default" in this field.</summary>
    private static bool IsUsableThumbnail(string? thumbnail) =>
        !string.IsNullOrWhiteSpace(thumbnail) &&
        thumbnail.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    private sealed record RedditListing
    {
        [JsonPropertyName("data")]
        public ListingData? Data { get; init; }
    }

    private sealed record ListingData
    {
        [JsonPropertyName("children")]
        public List<ListingChild>? Children { get; init; }
    }

    private sealed record ListingChild
    {
        [JsonPropertyName("data")]
        public RedditPost? Data { get; init; }
    }

    private sealed record RedditPost
    {
        [JsonPropertyName("id")]
        public string? Id { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("permalink")]
        public string? Permalink { get; init; }

        [JsonPropertyName("author")]
        public string? Author { get; init; }

        [JsonPropertyName("selftext")]
        public string? Selftext { get; init; }

        [JsonPropertyName("thumbnail")]
        public string? Thumbnail { get; init; }

        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("num_comments")]
        public int NumComments { get; init; }

        [JsonPropertyName("created_utc")]
        public double CreatedUtc { get; init; }

        [JsonPropertyName("is_self")]
        public bool IsSelf { get; init; }

        [JsonPropertyName("stickied")]
        public bool Stickied { get; init; }
    }
}
