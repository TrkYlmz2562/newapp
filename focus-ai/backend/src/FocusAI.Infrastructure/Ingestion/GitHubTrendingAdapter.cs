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
/// "GitHub Trending" from PRD section 6, built on the GitHub search API rather
/// than the trending page.
/// </summary>
/// <remarks>
/// GitHub publishes no trending API and the HTML page has no stable contract, so
/// this approximates it with "repositories created recently, ordered by stars" —
/// which is the signal the trending page is a proxy for anyway, and it comes
/// with a documented, rate-limited endpoint.
/// </remarks>
public sealed class GitHubTrendingAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<IngestionOptions> options,
    ILogger<GitHubTrendingAdapter> logger) : IFeedAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IngestionOptions _options = options.Value;

    public bool CanHandle(Source source) => source.Kind == SourceKind.GitHubTrending;

    public async Task<FeedFetchResult> FetchAsync(Source source, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpClientFactory.CreateClient(IngestionServiceNames.HttpClient);

            // FeedUrl may carry a custom query (e.g. a language filter); otherwise
            // fall back to a sensible default window.
            var url = source.FeedUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? source.FeedUrl
                : BuildDefaultQuery(_options.MaxItemsPerFeed);

            var result = await http.GetFromJsonAsync<SearchResult>(url, JsonOptions, cancellationToken);

            var items = (result?.Items ?? [])
                .Where(repo => !string.IsNullOrWhiteSpace(repo.FullName))
                .Take(_options.MaxItemsPerFeed)
                .Select(repo => new FeedItem
                {
                    ExternalId = repo.Id.ToString(),
                    Url = repo.HtmlUrl ?? $"https://github.com/{repo.FullName}",
                    Title = $"{repo.FullName}: {repo.Description ?? "yeni depo"}",
                    Author = repo.Owner?.Login,
                    Excerpt = repo.Description,
                    Content = BuildDescription(repo),
                    PublishedAt = repo.CreatedAt,
                    EngagementScore = repo.StargazersCount
                })
                .ToList();

            return new FeedFetchResult { Items = items };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI GitHub trending fetch failed for {SourceSlug}", source.Slug);
            return FeedFetchResult.Failure(ex.Message);
        }
    }

    private static string BuildDefaultQuery(int perPage)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-14).ToString("yyyy-MM-dd");
        return "https://api.github.com/search/repositories" +
               $"?q=created:>{since}+stars:>100&sort=stars&order=desc&per_page={Math.Clamp(perPage, 1, 100)}";
    }

    private static string BuildDescription(Repository repo)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(repo.Description))
        {
            parts.Add(repo.Description);
        }

        if (!string.IsNullOrWhiteSpace(repo.Language))
        {
            parts.Add($"Dil: {repo.Language}.");
        }

        parts.Add($"{repo.StargazersCount} yıldız, {repo.ForksCount} fork.");

        if (repo.Topics is { Count: > 0 })
        {
            parts.Add($"Konular: {string.Join(", ", repo.Topics.Take(10))}.");
        }

        return string.Join(' ', parts);
    }

    private sealed record SearchResult
    {
        [JsonPropertyName("items")]
        public List<Repository>? Items { get; init; }
    }

    private sealed record Repository
    {
        [JsonPropertyName("id")]
        public long Id { get; init; }

        [JsonPropertyName("full_name")]
        public string? FullName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("language")]
        public string? Language { get; init; }

        [JsonPropertyName("stargazers_count")]
        public int StargazersCount { get; init; }

        [JsonPropertyName("forks_count")]
        public int ForksCount { get; init; }

        [JsonPropertyName("topics")]
        public List<string>? Topics { get; init; }

        [JsonPropertyName("created_at")]
        public DateTimeOffset CreatedAt { get; init; }

        [JsonPropertyName("owner")]
        public RepositoryOwner? Owner { get; init; }
    }

    private sealed record RepositoryOwner
    {
        [JsonPropertyName("login")]
        public string? Login { get; init; }
    }
}
