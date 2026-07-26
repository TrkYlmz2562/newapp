using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusAI.Application.Features.Ingestion;

/// <summary>
/// One-off repair for articles that were ingested before the extractor learned to
/// read og:image. Fetches each page once and keeps only the lead image, then
/// lifts images onto stories that are still missing a hero.
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
    IDateTimeProvider clock,
    ILogger<BackfillImagesCommandHandler> logger)
    : IRequestHandler<BackfillImagesCommand, ImageBackfillReportDto>
{
    /// <summary>Deliberately low — we are a guest on these servers.</summary>
    private const int Concurrency = 4;

    /// <summary>
    /// Bounds one admin call's wall clock. Each page can cost the ingestion
    /// client's full retry budget, so a large batch would sit past any proxy's
    /// idle timeout and the operator would never see the result.
    /// </summary>
    private const int MaxBatch = 100;

    public async Task<ImageBackfillReportDto> Handle(
        BackfillImagesCommand request,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(request.BatchSize, 1, MaxBatch);
        var now = clock.UtcNow;

        // Only pages never attempted, and only articles attached to a story —
        // an orphaned article has no card to improve. Newest first: the top of
        // the feed is what a reader actually sees.
        var articles = await db.Articles
            .Where(a => a.ImageUrl == null && a.ImageCheckedAt == null && a.StoryId != null)
            .OrderByDescending(a => a.PublishedAt)
            .Take(take)
            .ToListAsync(cancellationToken);

        var found = 0;
        var noImage = 0;
        var fetchFailed = 0;

        if (articles.Count > 0)
        {
            using var throttle = new SemaphoreSlim(Concurrency);

            var results = await Task.WhenAll(articles.Select(async article =>
            {
                await throttle.WaitAsync(cancellationToken);
                try
                {
                    var page = await contentExtractor.ExtractAsync(article.Url, cancellationToken);
                    return (article, page.ImageUrl, Failed: false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogDebug(ex, "FocusAI image backfill failed for {Url}", article.Url);
                    return (article, ImageUrl: null, Failed: true);
                }
                finally
                {
                    throttle.Release();
                }
            }));

            foreach (var (article, imageUrl, failed) in results)
            {
                // Stamped whether or not anything was found: this is what stops the
                // next run re-fetching the same dead pages forever.
                article.ImageCheckedAt = now;

                if (!string.IsNullOrWhiteSpace(imageUrl))
                {
                    article.ImageUrl = imageUrl;
                    found++;
                }
                else if (failed)
                {
                    fetchFailed++;
                }
                else
                {
                    noImage++;
                }
            }

            // Committed before the story pass so a timeout during it cannot throw
            // away the fetches we just paid for.
            await db.SaveChangesAsync(cancellationToken);
        }

        var storiesUpdated = await LiftImagesOntoStoriesAsync(cancellationToken);

        var remaining = await db.Articles
            .CountAsync(
                a => a.ImageUrl == null && a.ImageCheckedAt == null && a.StoryId != null,
                cancellationToken);

        logger.LogInformation(
            "FocusAI image backfill: {Found} found, {NoImage} without an image, {Failed} unreachable, " +
            "{Stories} stories updated, {Remaining} articles left",
            found,
            noImage,
            fetchFailed,
            storiesUpdated,
            remaining);

        return new ImageBackfillReportDto(articles.Count, found, noImage, fetchFailed, storiesUpdated, remaining);
    }

    /// <summary>
    /// Gives a hero image to every story that has one available on a member
    /// article. Runs unconditionally and costs no HTTP: clustering only ever
    /// lifts the *primary* article's image, so a story whose primary has none
    /// stays blank even when a merged article carries a perfectly good picture.
    /// </summary>
    private async Task<int> LiftImagesOntoStoriesAsync(CancellationToken cancellationToken)
    {
        var stories = await db.Stories
            .Where(s => s.HeroImageUrl == null && s.Articles.Any(a => a.ImageUrl != null))
            .Include(s => s.Articles)
            .ToListAsync(cancellationToken);

        var updated = 0;

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
            updated++;
        }

        if (updated > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return updated;
    }
}
