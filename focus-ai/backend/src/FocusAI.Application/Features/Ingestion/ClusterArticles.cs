using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Clustering;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusAI.Application.Features.Ingestion;

/// <summary>
/// Stages 4-6 of the PRD section 13 pipeline: Duplicate Detection → Embedding →
/// Vector Database. Every normalised article either joins an existing story or
/// opens a new one.
/// </summary>
public sealed record ClusterArticlesCommand(int BatchSize = 200) : IRequest<IngestionReportDto>;

public sealed class ClusterArticlesCommandHandler(
    IApplicationDbContext db,
    IEmbeddingService embeddings,
    IDateTimeProvider clock,
    ILogger<ClusterArticlesCommandHandler> logger)
    : IRequestHandler<ClusterArticlesCommand, IngestionReportDto>
{
    public async Task<IngestionReportDto> Handle(
        ClusterArticlesCommand request,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(request.BatchSize, 1, 1000);
        var now = clock.UtcNow;

        var pending = await db.Articles
            .Where(a => a.Status == ArticleStatus.Normalized && a.StoryId == null)
            .OrderBy(a => a.PublishedAt)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (pending.Count == 0)
        {
            return new IngestionReportDto(0, 0, 0, 0, 0, 0, []);
        }

        await EmbedAsync(pending, cancellationToken);

        // Load the candidate window once. Clustering is O(pending × window), and
        // the window is bounded by time, so this stays comfortably small.
        var windowStart = pending.Min(a => a.PublishedAt) - StoryClusterer.DefaultWindow;

        var openStories = await db.Stories
            .Where(s => s.LastActivityAt >= windowStart && s.Status != StoryStatus.Archived)
            .Select(s => new
            {
                s.Id,
                s.PublishedAt,
                s.LastActivityAt,
                s.Embedding,
                PrimarySimHash = s.Articles
                    .Where(a => a.IsPrimary)
                    .Select(a => a.SimHash)
                    .FirstOrDefault(),
                Hashes = s.Articles.Select(a => a.ContentHash).ToList()
            })
            .ToListAsync(cancellationToken);

        var targets = openStories
            .Select(s => new ClusterTarget
            {
                StoryId = s.Id,
                SimHash = s.PrimarySimHash,
                Embedding = s.Embedding,
                PublishedAt = s.PublishedAt,
                LastActivityAt = s.LastActivityAt,
                ContentHashes = s.Hashes.ToHashSet()
            })
            .ToList();

        var merged = 0;
        var storiesCreated = 0;
        var touchedStoryIds = new HashSet<Guid>();
        var newStories = new List<Story>();

        foreach (var article in pending)
        {
            var candidate = new ClusterCandidate
            {
                ArticleId = article.Id,
                SourceId = article.SourceId,
                ContentHash = article.ContentHash,
                SimHash = article.SimHash,
                Embedding = article.Embedding,
                PublishedAt = article.PublishedAt
            };

            var decision = StoryClusterer.FindCluster(candidate, targets);

            if (decision is { IsMatch: true, StoryId: { } storyId })
            {
                article.StoryId = storyId;
                article.Status = ArticleStatus.Clustered;
                merged++;
                touchedStoryIds.Add(storyId);

                var target = targets.First(t => t.StoryId == storyId);
                targets[targets.IndexOf(target)] = target with
                {
                    LastActivityAt = article.PublishedAt > target.LastActivityAt
                        ? article.PublishedAt
                        : target.LastActivityAt,
                    ContentHashes = new HashSet<string>(target.ContentHashes) { article.ContentHash }
                };

                continue;
            }

            var story = new Story
            {
                // Disambiguated against the batch and the database below, before saving.
                Slug = Slugger.SlugifyUnique(article.Title, Guid.NewGuid()),
                Title = article.Title,
                Dek = article.Excerpt is { Length: > 0 } ? Truncate(article.Excerpt, 300) : null,
                Category = ContentCategory.Unknown,
                Status = StoryStatus.Draft,
                PublishedAt = article.PublishedAt,
                LastActivityAt = article.PublishedAt,
                PrimaryArticleId = article.Id,
                HeroImageUrl = article.ImageUrl,
                Embedding = article.Embedding,
                SourceCount = 1,
                ReadingMinutes = 1,
                CreatedAt = now
            };

            db.Stories.Add(story);

            article.StoryId = story.Id;
            article.IsPrimary = true;
            article.Status = ArticleStatus.Clustered;

            targets.Add(new ClusterTarget
            {
                StoryId = story.Id,
                SimHash = article.SimHash,
                Embedding = article.Embedding,
                PublishedAt = article.PublishedAt,
                LastActivityAt = article.PublishedAt,
                ContentHashes = new HashSet<string> { article.ContentHash }
            });

            newStories.Add(story);
            storiesCreated++;
            touchedStoryIds.Add(story.Id);
        }

        await EnsureUniqueSlugsAsync(newStories, cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await RefreshAggregatesAsync(touchedStoryIds, cancellationToken);

        logger.LogInformation(
            "FocusAI clustered {Pending} articles into {New} new and {Merged} existing stories",
            pending.Count,
            storiesCreated,
            merged);

        return new IngestionReportDto(0, 0, pending.Count, merged, storiesCreated, 0, []);
    }

    /// <summary>
    /// Guarantees slug uniqueness before hitting the unique index.
    /// </summary>
    /// <remarks>
    /// Two guards, because the failure modes differ. Within a batch, syndicated
    /// copies of one headline routinely produce the same base slug, so an
    /// in-memory set is needed. Across batches, an identical headline may have
    /// been published weeks ago, so one round-trip checks the stored slugs too.
    /// Conflicts are resolved by re-rolling the random suffix rather than by
    /// appending a counter, which would leak batch position into the permalink.
    /// </remarks>
    private async Task EnsureUniqueSlugsAsync(
        IReadOnlyList<Story> stories,
        CancellationToken cancellationToken)
    {
        if (stories.Count == 0)
        {
            return;
        }

        var candidates = stories.Select(s => s.Slug).ToList();
        var taken = (await db.Stories
                .AsNoTracking()
                .Where(s => candidates.Contains(s.Slug))
                .Select(s => s.Slug)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var story in stories)
        {
            var attempts = 0;
            while (!taken.Add(story.Slug))
            {
                story.Slug = Slugger.SlugifyUnique(story.Title, Guid.NewGuid());

                // 32 bits of randomness makes repeated collisions vanishingly
                // unlikely; the ceiling exists so a pathological case cannot spin.
                if (++attempts >= 5)
                {
                    story.Slug = story.Id.ToString("N");
                    taken.Add(story.Slug);
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Embeds in one batch. A failure here is not fatal: clustering still works
    /// from SimHash alone, just with less semantic recall.
    /// </summary>
    private async Task EmbedAsync(IReadOnlyList<Article> articles, CancellationToken cancellationToken)
    {
        var needing = articles.Where(a => a.Embedding is null or { Length: 0 }).ToList();
        if (needing.Count == 0)
        {
            return;
        }

        try
        {
            var texts = needing
                .Select(a => Truncate($"{a.Title}\n\n{a.BestText()}", 6000)!)
                .ToList();

            var vectors = await embeddings.EmbedBatchAsync(texts, cancellationToken);

            for (var i = 0; i < needing.Count && i < vectors.Count; i++)
            {
                needing[i].Embedding = vectors[i];
                needing[i].Status = ArticleStatus.Embedded;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI embedding failed; clustering will fall back to SimHash only");
        }
    }

    /// <summary>Recomputes denormalised counters and the centroid for every touched cluster.</summary>
    private async Task RefreshAggregatesAsync(
        IReadOnlyCollection<Guid> storyIds,
        CancellationToken cancellationToken)
    {
        if (storyIds.Count == 0)
        {
            return;
        }

        var stories = await db.Stories
            .Where(s => storyIds.Contains(s.Id))
            .Include(s => s.Articles)
            .ToListAsync(cancellationToken);

        var sourceIds = stories.SelectMany(s => s.Articles.Select(a => a.SourceId)).Distinct().ToList();
        var sources = await db.Sources
            .Where(s => sourceIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        foreach (var story in stories)
        {
            var members = story.Articles.ToList();
            if (members.Count == 0)
            {
                continue;
            }

            story.RefreshAggregates(members, sources);

            var vectors = members
                .Where(a => a.Embedding is { Length: > 0 })
                .Select(a => a.Embedding!)
                .ToList();

            if (vectors.Count > 0)
            {
                story.Embedding = VectorMath.Centroid(vectors);
            }

            if (!members.Any(a => a.IsPrimary))
            {
                // Prefer the official source's telling, then the earliest report.
                var official = sources.Where(s => s.IsOfficial).Select(s => s.Id).ToHashSet();
                var primary = members
                    .OrderByDescending(a => official.Contains(a.SourceId))
                    .ThenBy(a => a.PublishedAt)
                    .First();

                primary.IsPrimary = true;
                story.PrimaryArticleId = primary.Id;
                story.HeroImageUrl ??= primary.ImageUrl;
            }

            // New coverage invalidates the existing summary and scores.
            if (story.Status == StoryStatus.Published && members.Count > 1)
            {
                story.Status = StoryStatus.Enriching;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
