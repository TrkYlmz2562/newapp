using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Entities.Content;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FocusAI.Infrastructure.Search;

/// <summary>
/// Meilisearch index for keyword search (PRD section 12). Talks the REST API
/// directly rather than taking a client dependency — the surface used here is
/// three endpoints.
/// </summary>
public sealed class MeilisearchIndex(
    IHttpClientFactory httpClientFactory,
    IOptions<SearchOptions> options,
    ILogger<MeilisearchIndex> logger) : ISearchIndex
{
    public const string HttpClientName = "focus-ai-search";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly SearchOptions _options = options.Value;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.Url);

    public async Task IndexAsync(IReadOnlyList<Story> stories, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || stories.Count == 0)
        {
            return;
        }

        var documents = stories.Select(story => new
        {
            id = story.Id.ToString("N"),
            storyId = story.Id,
            slug = story.Slug,
            title = story.Title,
            dek = story.Dek,
            summary = story.Summary?.Summary,
            whyItMatters = story.Summary?.WhyItMatters,
            category = story.Category.ToString(),
            trustScore = story.TrustScore,
            importanceScore = story.ImportanceScore,
            publishedAt = story.PublishedAt.ToUnixTimeSeconds(),
            topics = story.Topics.Select(t => t.Topic?.Slug).Where(s => s is not null).ToArray()
        }).ToList();

        var http = httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.PutAsJsonAsync(
            $"indexes/{_options.IndexName}/documents?primaryKey=id",
            documents,
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            logger.LogWarning(
                "FocusAI Meilisearch indexing returned {StatusCode}: {Body}",
                (int)response.StatusCode,
                body.Length > 300 ? body[..300] : body);
        }
    }

    public async Task DeleteAsync(Guid storyId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
        {
            return;
        }

        var http = httpClientFactory.CreateClient(HttpClientName);
        using var response = await http.DeleteAsync(
            $"indexes/{_options.IndexName}/documents/{storyId:N}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning(
                "FocusAI Meilisearch delete returned {StatusCode} for {StoryId}",
                (int)response.StatusCode,
                storyId);
        }
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        int take,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(query))
        {
            return [];
        }

        try
        {
            var http = httpClientFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsJsonAsync(
                $"indexes/{_options.IndexName}/search",
                new { q = query, limit = Math.Clamp(take, 1, 1000) },
                JsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return [];
            }

            var parsed = await response.Content.ReadFromJsonAsync<SearchResponse>(
                JsonOptions, cancellationToken);

            return (parsed?.Hits ?? [])
                .Where(hit => hit.StoryId != Guid.Empty)
                // Meilisearch returns results already ranked; the descending index
                // preserves that order once the caller re-sorts by score.
                .Select((hit, index) => new SearchHit(hit.StoryId, 1d - index / 1000d))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The caller falls back to a database query when this returns empty.
            logger.LogWarning(ex, "FocusAI Meilisearch query failed, falling back to database search");
            return [];
        }
    }

    private sealed record SearchResponse
    {
        [JsonPropertyName("hits")]
        public List<SearchDocument>? Hits { get; init; }
    }

    private sealed record SearchDocument
    {
        [JsonPropertyName("storyId")]
        public Guid StoryId { get; init; }
    }
}

/// <summary>Used when no search host is configured; every caller has a database fallback.</summary>
public sealed class NullSearchIndex : ISearchIndex
{
    public bool IsEnabled => false;

    public Task IndexAsync(IReadOnlyList<Story> stories, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task DeleteAsync(Guid storyId, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<SearchHit>> SearchAsync(
        string query,
        int take,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SearchHit>>([]);
}
