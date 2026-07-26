using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Scoring;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Catalog;

/// <summary>
/// Health of every registered source, so a silently-dead feed is visible instead
/// of just quietly contributing nothing.
/// </summary>
public sealed record GetSourceHealthQuery : IRequest<SourceHealthReportDto>;

public sealed class GetSourceHealthQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IDateTimeProvider clock) : IRequestHandler<GetSourceHealthQuery, SourceHealthReportDto>
{
    /// <summary>
    /// Article timestamps sampled per source to learn its rhythm. Enough to smooth
    /// out a burst, few enough that a source's rhythm from two years ago does not
    /// dominate its current one.
    /// </summary>
    private const int CadenceSampleSize = 25;

    public async Task<SourceHealthReportDto> Handle(
        GetSourceHealthQuery request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var since = now.AddDays(-30);

        var sources = await db.Sources
            .AsNoTracking()
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.Slug,
                s.WebsiteUrl,
                s.Category,
                s.IsOfficial,
                s.IsEnabled,
                s.TrustWeight,
                s.Language,
                s.FetchIntervalMinutes,
                s.LastFetchedAt,
                s.LastSucceededAt,
                s.ConsecutiveFailures,
                s.LastError,
                s.IconUrl,
                ArticleCount30d = s.Articles.Count(a => a.PublishedAt >= since),
                // Sampled in the database rather than by loading every article:
                // some sources have tens of thousands.
                RecentTimes = s.Articles
                    .OrderByDescending(a => a.PublishedAt)
                    .Select(a => a.PublishedAt)
                    .Take(CadenceSampleSize)
                    .ToList()
            })
            .ToListAsync(cancellationToken);

        var favorites = currentUser.UserId is { } userId
            ? (await db.UserFavoriteSources
                .AsNoTracking()
                .Where(f => f.UserProfile!.UserId == userId)
                .Select(f => f.SourceId)
                .ToListAsync(cancellationToken))
                .ToHashSet()
            : [];

        var items = sources
            .Select(s =>
            {
                var health = SourceHealthEvaluator.Evaluate(new SourceHealthInput
                {
                    IsEnabled = s.IsEnabled,
                    FetchIntervalMinutes = s.FetchIntervalMinutes,
                    LastFetchedAt = s.LastFetchedAt,
                    LastSucceededAt = s.LastSucceededAt,
                    ConsecutiveFailures = s.ConsecutiveFailures,
                    LastError = s.LastError,
                    NewestArticleAt = s.RecentTimes.Count > 0 ? s.RecentTimes[0] : null,
                    RecentArticleTimes = s.RecentTimes,
                    Now = now
                });

                return new SourceHealthDto(
                    s.Id,
                    s.Name,
                    s.Slug,
                    s.WebsiteUrl,
                    s.Category,
                    s.IsOfficial,
                    s.IsEnabled,
                    s.TrustWeight,
                    s.Language,
                    s.IconUrl,
                    health.Status,
                    health.Reason,
                    s.LastSucceededAt,
                    s.ConsecutiveFailures,
                    s.RecentTimes.Count > 0 ? s.RecentTimes[0] : null,
                    s.ArticleCount30d,
                    health.TypicalGapHours,
                    favorites.Contains(s.Id));
            })
            // Problems first, then the loudest problems within each group. Ordering a
            // health report alphabetically would bury the thing it exists to show.
            .OrderBy(i => StatusRank(i.Status))
            .ThenByDescending(i => i.ConsecutiveFailures)
            .ThenBy(i => i.NewestArticleAt ?? DateTimeOffset.MinValue)
            .ThenBy(i => i.Name)
            .ToList();

        return new SourceHealthReportDto(
            items,
            items.Count(i => i.Status == SourceHealthStatus.Healthy),
            items.Count(i => i.Status == SourceHealthStatus.Stale),
            items.Count(i => i.Status == SourceHealthStatus.Failing),
            items.Count(i => i.Status == SourceHealthStatus.Disabled),
            now);
    }

    private static int StatusRank(SourceHealthStatus status) => status switch
    {
        SourceHealthStatus.Failing => 0,
        SourceHealthStatus.Stale => 1,
        SourceHealthStatus.Unknown => 2,
        SourceHealthStatus.Healthy => 3,
        SourceHealthStatus.Disabled => 4,
        _ => 5
    };
}

/// <summary>Turns a source on or off. Admin-only: it changes what everyone sees.</summary>
public sealed record SetSourceEnabledCommand(Guid SourceId, bool Enabled) : IRequest<bool>;

public sealed class SetSourceEnabledCommandHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock) : IRequestHandler<SetSourceEnabledCommand, bool>
{
    public async Task<bool> Handle(SetSourceEnabledCommand request, CancellationToken cancellationToken)
    {
        var source = await db.Sources.FirstOrDefaultAsync(s => s.Id == request.SourceId, cancellationToken)
            ?? throw Common.Exceptions.NotFoundException.For("Kaynak", request.SourceId);

        source.IsEnabled = request.Enabled;
        source.UpdatedAt = clock.UtcNow;

        if (request.Enabled)
        {
            // Re-enabling has to clear the back-off, otherwise the scheduler keeps
            // resting a source the operator just asked for — for up to 24 intervals.
            source.ConsecutiveFailures = 0;
            source.LastError = null;
        }

        await db.SaveChangesAsync(cancellationToken);
        return source.IsEnabled;
    }
}
