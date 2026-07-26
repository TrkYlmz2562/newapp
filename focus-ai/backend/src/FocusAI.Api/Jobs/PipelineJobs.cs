using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Features.Digests;
using FocusAI.Application.Features.Ingestion;
using FocusAI.Application.Features.Trends;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Api.Jobs;

/// <summary>
/// The scheduled side of the product: the pipeline that has to have run before
/// anyone opens the app at 08:00 (PRD section 5.1).
/// </summary>
public sealed class PipelineJobs(
    ISender sender,
    IApplicationDbContext db,
    IDateTimeProvider clock,
    ILogger<PipelineJobs> logger)
{
    /// <summary>Poll → cluster → enrich. Runs continuously through the day.</summary>
    public async Task RunIngestionAsync(CancellationToken cancellationToken = default)
    {
        var ingest = await sender.Send(new IngestSourcesCommand(), cancellationToken);
        var cluster = await sender.Send(new ClusterArticlesCommand(), cancellationToken);
        var enrich = await sender.Send(new EnrichStoriesCommand(), cancellationToken);

        logger.LogInformation(
            "FocusAI pipeline cycle: {Articles} articles, {Merged} merged, {Stories} stories, {Published} published",
            ingest.ArticlesCreated,
            cluster.DuplicatesMerged,
            cluster.StoriesCreated,
            enrich.StoriesCreated);
    }

    /// <summary>
    /// Builds every reader's daily edition. Runs hourly and only picks up users
    /// whose local digest hour has just struck, which is what makes a single
    /// schedule serve readers in any time zone.
    /// </summary>
    public async Task BuildDailyDigestsAsync(CancellationToken cancellationToken = default)
    {
        var now = clock.UtcNow;

        // The shared edition first: anonymous visitors and brand-new accounts
        // read this one.
        await sender.Send(new GenerateDigestCommand(null, null, DigestPeriod.Daily, Force: true), cancellationToken);

        var readers = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive)
            .Select(u => new
            {
                u.Id,
                u.TimeZone,
                DigestHour = u.Profile != null ? u.Profile.DailyDigestHour : 8
            })
            .ToListAsync(cancellationToken);

        var built = 0;

        foreach (var reader in readers)
        {
            var localHour = clock.ToLocal(now, reader.TimeZone).Hour;
            if (localHour != reader.DigestHour)
            {
                continue;
            }

            try
            {
                await sender.Send(
                    new GenerateDigestCommand(reader.Id, null, DigestPeriod.Daily, Force: true),
                    cancellationToken);

                built++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One reader's bad state must not stop the rest of the run.
                logger.LogError(ex, "FocusAI daily digest failed for user {UserId}", reader.Id);
            }
        }

        logger.LogInformation("FocusAI built {Count} personal daily digests", built);
    }

    public async Task BuildWeeklyDigestsAsync(CancellationToken cancellationToken = default)
    {
        await sender.Send(
            new GenerateDigestCommand(null, null, DigestPeriod.Weekly, Force: true),
            cancellationToken);

        var readers = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.NotificationSettings != null && u.NotificationSettings.WeeklyDigest)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        foreach (var userId in readers)
        {
            try
            {
                await sender.Send(
                    new GenerateDigestCommand(userId, null, DigestPeriod.Weekly, Force: true),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "FocusAI weekly digest failed for user {UserId}", userId);
            }
        }
    }

    public async Task RebuildTrendsAsync(CancellationToken cancellationToken = default)
    {
        await sender.Send(new RebuildTrendsCommand(DigestPeriod.Monthly), cancellationToken);
        await sender.Send(new RebuildTrendsCommand(DigestPeriod.Weekly), cancellationToken);
    }
}
