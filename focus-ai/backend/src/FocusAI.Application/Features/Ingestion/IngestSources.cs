using System.Security.Cryptography;
using System.Text;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Options;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FocusAI.Application.Features.Ingestion;

/// <summary>
/// Stage 1 of the PRD section 13 pipeline: RSS → Crawler → Normalize. Polls every
/// due source and lands raw articles, without any AI in the loop.
/// </summary>
public sealed record IngestSourcesCommand(Guid? SourceId = null, bool IgnoreSchedule = false)
    : IRequest<IngestionReportDto>;

public sealed class IngestSourcesCommandHandler(
    IApplicationDbContext db,
    IFeedAdapterResolver adapterResolver,
    IContentExtractor contentExtractor,
    IOptions<IngestionSettings> settings,
    IDateTimeProvider clock,
    ILogger<IngestSourcesCommandHandler> logger) : IRequestHandler<IngestSourcesCommand, IngestionReportDto>
{
    /// <summary>Feeds occasionally republish their whole archive; refuse to ingest history.</summary>
    private static readonly TimeSpan MaxItemAge = TimeSpan.FromDays(14);

    /// <summary>
    /// Below this, the feed gave us a teaser rather than an article, and it is
    /// worth fetching the page. Above it, the feed is full-text already.
    /// </summary>
    private const int ShortContentThreshold = 600;

    /// <summary>Parallel page fetches. Deliberately low — we are a guest on these servers.</summary>
    private const int ExtractionConcurrency = 4;

    public async Task<IngestionReportDto> Handle(
        IngestSourcesCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var sourceQuery = db.Sources.Where(s => s.IsEnabled);
        if (request.SourceId is { } sourceId)
        {
            sourceQuery = db.Sources.Where(s => s.Id == sourceId);
        }

        var sources = await sourceQuery.ToListAsync(cancellationToken);
        var due = request.IgnoreSchedule
            ? sources
            : sources.Where(s => s.IsDue(now)).ToList();

        var errors = new List<string>();
        var itemsFetched = 0;
        var created = 0;
        var failures = 0;

        foreach (var source in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var adapter = adapterResolver.Resolve(source);
                var result = await adapter.FetchAsync(source, cancellationToken);

                if (!result.Succeeded)
                {
                    failures++;
                    source.MarkFailure(now, result.Error ?? "Bilinmeyen hata");
                    errors.Add($"{source.Name}: {result.Error}");
                    continue;
                }

                source.MarkSuccess(now, result.ETag, result.LastModified);

                if (result.NotModified)
                {
                    continue;
                }

                itemsFetched += result.Items.Count;
                created += await PersistItemsAsync(source, result.Items, now, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // One broken feed must never abort the whole ingestion cycle.
                failures++;
                source.MarkFailure(now, ex.Message);
                errors.Add($"{source.Name}: {ex.Message}");
                logger.LogError(ex, "FocusAI ingestion failed for source {SourceSlug}", source.Slug);
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "FocusAI ingestion polled {Polled} sources, fetched {Fetched} items, created {Created} articles",
            due.Count,
            itemsFetched,
            created);

        return new IngestionReportDto(
            due.Count,
            itemsFetched,
            created,
            DuplicatesMerged: 0,
            StoriesCreated: 0,
            failures,
            errors);
    }

    private async Task<int> PersistItemsAsync(
        Source source,
        IReadOnlyList<FeedItem> items,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return 0;
        }

        var externalIds = items.Select(i => i.ExternalId).ToList();
        var canonicalUrls = items.Select(i => UrlNormalizer.Normalize(i.Url)).ToList();

        var known = await db.Articles
            .Where(a => a.SourceId == source.Id && externalIds.Contains(a.ExternalId))
            .Select(a => a.ExternalId)
            .ToListAsync(cancellationToken);

        var knownIds = known.ToHashSet();

        // Cross-source URL collision: the same canonical link arriving from an
        // aggregator is not new content, it is the same article.
        var knownUrls = (await db.Articles
                .Where(a => canonicalUrls.Contains(a.CanonicalUrl))
                .Select(a => a.CanonicalUrl)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        var seenThisBatch = new HashSet<string>(StringComparer.Ordinal);
        var accepted = new List<(FeedItem Item, string Canonical, DateTimeOffset PublishedAt)>();

        foreach (var item in items)
        {
            if (knownIds.Contains(item.ExternalId))
            {
                continue;
            }

            var canonical = UrlNormalizer.Normalize(item.Url);
            if (canonical.Length == 0 || knownUrls.Contains(canonical) || !seenThisBatch.Add(canonical))
            {
                continue;
            }

            var publishedAt = item.PublishedAt ?? now;
            if (now - publishedAt > MaxItemAge)
            {
                continue;
            }

            // A feed dated in the future is a clock problem, not a scoop.
            if (publishedAt > now.AddHours(1))
            {
                publishedAt = now;
            }

            accepted.Add((item, canonical, publishedAt));
        }

        if (accepted.Count == 0)
        {
            return 0;
        }

        // Extraction happens before hashing so de-duplication compares full
        // articles rather than teaser text, which two outlets often share verbatim.
        var pages = await ExtractPagesAsync(accepted, cancellationToken);

        foreach (var (item, canonical, publishedAt) in accepted)
        {
            pages.TryGetValue(canonical, out var page);

            var content = !string.IsNullOrWhiteSpace(page?.Text) ? page!.Text : item.Content;

            var text = string.IsNullOrWhiteSpace(content) ? item.Excerpt : content;

            db.Articles.Add(new Article
            {
                SourceId = source.Id,
                ExternalId = item.ExternalId,
                Url = item.Url,
                CanonicalUrl = canonical,
                Title = Truncate(item.Title, 500)!,
                Author = Truncate(item.Author, 200),
                Excerpt = Truncate(item.Excerpt, 4000),
                Content = Truncate(content, 50_000),
                // Feed-provided media wins (it is the publisher's explicit choice);
                // the page's og:image is the fallback. A story without either is fine
                // — the reader still sees it, with a category illustration on the card.
                ImageUrl = PickImage(item.ImageUrl, page?.ImageUrl),
                Language = source.Language,
                PublishedAt = publishedAt,
                FetchedAt = now,
                Status = ArticleStatus.Normalized,
                ContentHash = ComputeHash(item.Title, text),
                SimHash = SimHash.Compute($"{item.Title} {text}"),
                EngagementScore = item.EngagementScore,
                CommentCount = item.CommentCount,
                CreatedAt = now
            });
        }

        return accepted.Count;
    }

    /// <summary>
    /// Fetches article pages for items whose feed payload was only a teaser, taking
    /// both the body text and — for free, from the same fetch — a lead image. The
    /// fetch is NOT triggered just to hunt for an image: a full-text feed that omits
    /// an image is left as-is rather than hammering the publisher for a picture.
    /// Failures are silent by design — the feed excerpt remains the floor, and a
    /// paywalled or unreachable page must not cost us the item.
    /// </summary>
    private async Task<Dictionary<string, ExtractedArticle>> ExtractPagesAsync(
        IReadOnlyList<(FeedItem Item, string Canonical, DateTimeOffset PublishedAt)> accepted,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, ExtractedArticle>(StringComparer.Ordinal);

        if (!settings.Value.ExtractFullContent)
        {
            return results;
        }

        var needing = accepted
            .Where(a => (a.Item.Content?.Length ?? 0) < ShortContentThreshold)
            .ToList();

        if (needing.Count == 0)
        {
            return results;
        }

        using var throttle = new SemaphoreSlim(ExtractionConcurrency);

        var tasks = needing.Select(async entry =>
        {
            await throttle.WaitAsync(cancellationToken);
            try
            {
                var page = await contentExtractor.ExtractAsync(entry.Item.Url, cancellationToken);
                return (entry.Canonical, Page: page);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "FocusAI content extraction failed for {Url}", entry.Item.Url);
                return (entry.Canonical, Page: ExtractedArticle.Empty);
            }
            finally
            {
                throttle.Release();
            }
        });

        foreach (var (canonical, page) in await Task.WhenAll(tasks))
        {
            results[canonical] = page;
        }

        logger.LogDebug(
            "FocusAI extracted {Bodies} bodies and {Images} images from {Attempted} pages",
            results.Count(r => !string.IsNullOrWhiteSpace(r.Value.Text)),
            results.Count(r => !string.IsNullOrWhiteSpace(r.Value.ImageUrl)),
            needing.Count);

        return results;
    }

    /// <summary>First usable image URL: an absolute http(s) link that fits the column.</summary>
    private static string? PickImage(params string?[] candidates) =>
        candidates.FirstOrDefault(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            candidate.Length <= 2000 &&
            (candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
             candidate.StartsWith("https://", StringComparison.OrdinalIgnoreCase)));

    private static string ComputeHash(string title, string? body)
    {
        var normalized = TextNormalizer.Normalize($"{title} {body}");
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes);
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
