using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusAI.Application.Features.Ingestion;

/// <summary>
/// One-off repair for articles that were ingested before the extractor learned to
/// read og:image. Fetches each page once and keeps only the lead image, then
/// lifts it onto any story still missing a hero image.
/// </summary>
/// <remarks>
/// Deliberately manual and batched rather than a recurring job. Routine ingestion
/// only takes an image from a page it was already fetching for its body text; a
/// story is never fetched, delayed or down-ranked just to find a picture. This
/// command is the explicit exception, run by an operator against the backlog.
/// </remarks>
public sealed record BackfillImagesCommand(int BatchSize = 50) : IRequest<ImageBackfillReportDto>;

public sealed class BackfillImagesCommandHandler(
    IApplicationDbContext db,
    IContentExtractor contentExtractor,
    ILogger<BackfillImagesCommandHandler> logger)
    : IRequestHandler<BackfillImagesCommand, ImageBackfillReportDto>
{
    /// <summary>Deliberately low — we are a guest on these servers.</summary>
    private const int Concurrency = 4;

    private const int MaxBatch = 200;

    public async Task<ImageBackfillReportDto> Handle(
        BackfillImagesCommand request,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.BatchSize, 1, MaxBatch);

        // Newest first: the top of the feed is what a reader actually sees.
        var articles = await db.Articles
            .Where(a => a.ImageUrl == null)
            .OrderByDescending(a => a.PublishedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        var found = 0;
        var failed = 0;

        if (articles.Count > 0)
        {
            using var throttle = new SemaphoreSlim(Concurrency);

            var results = await Task.WhenAll(articles.Select(async article =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    var page = await contentExtractor.ExtractAsync(article.Url, cancellationToken);
                    return (article, page.ImageUrl);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "FocusAI image backfill failed for {Url}", article.Url);
                    return (article, null);
                }
                finally
                {
                    throttle.Release();
                }
            }));

            foreach (var (article, imageUrl) in results)
            {
                if (string.IsNullOrWhiteSpace(imageUrl))
                {
                    failed++;
                    continue;
                }

                article.ImageUrl = imageUrl;
                found++;
            }
        }

        // Lift the newly-found images onto their stories. Mirrors the clustering
        // rule: an existing hero image is never overwritten, and the primary
        // article wins over the rest of the cluster.
        var storiesUpdated = 0;

        if (found > 0)
        {
            var storyIds = articles
                .Where(a => a.StoryId.HasValue && !string.IsNullOrWhiteSpace(a.ImageUrl))
                .Select(a => a.StoryId!.Value)
                .Distinct()
                .ToList();

            var stories = await db.Stories
                .Where(s => storyIds.Contains(s.Id) && s.HeroImageUrl == null)
                .Include(s => s.Articles)
                .ToListAsync(cancellationToken);

            foreach (var story in stories)
            {
                var image = story.Articles
                    .OrderByDescending(a => a.IsPrimary)
                    .ThenByDescending(a => a.PublishedAt)
                    .Select(a => a.ImageUrl)
                    .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

                if (string.IsNullOrWhiteSpace(image))
                {
                    continue;
                }

                story.HeroImageUrl = image;
                storiesUpdated++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var remaining = await db.Articles.CountAsync(a => a.ImageUrl == null, cancellationToken);

        logger.LogInformation(
            "FocusAI image backfill: {Found}/{Scanned} images found, {Stories} stories updated, {Remaining} articles left",
            found,
            articles.Count,
            storiesUpdated,
            remaining);

        return new ImageBackfillReportDto(articles.Count, found, failed, storiesUpdated, remaining);
    }
}
