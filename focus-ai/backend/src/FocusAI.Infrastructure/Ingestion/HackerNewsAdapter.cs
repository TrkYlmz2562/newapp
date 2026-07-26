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
/// Hacker News via the official Firebase API. Worth its own adapter rather than
/// using the RSS mirror because the API exposes points and comment counts, which
/// are the community-trust signal in the scoring model.
/// </summary>
public sealed class HackerNewsAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<IngestionOptions> options,
    ILogger<HackerNewsAdapter> logger) : IFeedAdapter
{
    private const string ApiRoot = "https://hacker-news.firebaseio.com/v0";

    /// <summary>Below this, a story has not cleared the noise floor of /new.</summary>
    private const int MinimumPoints = 25;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IngestionOptions _options = options.Value;

    public bool CanHandle(Source source) => source.Kind == SourceKind.HackerNews;

    public async Task<FeedFetchResult> FetchAsync(Source source, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpClientFactory.CreateClient(IngestionServiceNames.HttpClient);

            var ids = await http.GetFromJsonAsync<long[]>(
                $"{ApiRoot}/topstories.json", JsonOptions, cancellationToken);

            if (ids is null || ids.Length == 0)
            {
                return FeedFetchResult.Empty();
            }

            var wanted = ids.Take(_options.MaxItemsPerFeed).ToList();

            // Item lookups are one request each; bounded concurrency keeps the
            // cycle fast without hammering the API.
            using var throttle = new SemaphoreSlim(8);
            var tasks = wanted.Select(async id =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    return await http.GetFromJsonAsync<HackerNewsItem>(
                        $"{ApiRoot}/item/{id}.json", JsonOptions, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "FocusAI failed to read Hacker News item {ItemId}", id);
                    return null;
                }
                finally
                {
                    throttle.Release();
                }
            });

            var items = (await Task.WhenAll(tasks))
                .Where(item => item is { Type: "story", Score: >= MinimumPoints })
                .Where(item => !string.IsNullOrWhiteSpace(item!.Title))
                .Select(item => new FeedItem
                {
                    ExternalId = item!.Id.ToString(),
                    // Ask HN and Show HN posts have no external URL; link to the thread.
                    Url = string.IsNullOrWhiteSpace(item.Url)
                        ? $"https://news.ycombinator.com/item?id={item.Id}"
                        : item.Url,
                    Title = item.Title!,
                    Author = item.By,
                    Excerpt = HtmlText.ToPlainText(item.Text),
                    PublishedAt = DateTimeOffset.FromUnixTimeSeconds(item.Time),
                    EngagementScore = item.Score,
                    CommentCount = item.Descendants
                })
                .ToList();

            return new FeedFetchResult { Items = items };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI Hacker News fetch failed");
            return FeedFetchResult.Failure(ex.Message);
        }
    }

    private sealed record HackerNewsItem
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("title")]
        public string? Title { get; init; }

        [JsonPropertyName("url")]
        public string? Url { get; init; }

        [JsonPropertyName("by")]
        public string? By { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }

        [JsonPropertyName("score")]
        public int Score { get; init; }

        [JsonPropertyName("descendants")]
        public int Descendants { get; init; }

        [JsonPropertyName("time")]
        public long Time { get; init; }
    }
}
