using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Application.Common.Services;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Digests;

/// <summary>
/// "Bugünün Özeti" (PRD 5.1) and the weekly edition (5.8). Generates on demand
/// when the scheduled job has not produced one yet, so the screen is never empty.
/// </summary>
public sealed record GetDigestQuery(DigestPeriod Period = DigestPeriod.Daily, DateOnly? Date = null)
    : IRequest<DigestDto?>;

public sealed class GetDigestQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReaderContextFactory readerContextFactory,
    IDateTimeProvider clock,
    ISender sender) : IRequestHandler<GetDigestQuery, DigestDto?>
{
    public async Task<DigestDto?> Handle(GetDigestQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId;
        var snapshot = await readerContextFactory.BuildAsync(userId, cancellationToken);
        var date = request.Date ?? clock.TodayIn(snapshot.TimeZone);

        var digest = await LoadAsync(userId, date, request.Period, cancellationToken);

        if (digest is null)
        {
            await sender.Send(new GenerateDigestCommand(userId, date, request.Period), cancellationToken);
            digest = await LoadAsync(userId, date, request.Period, cancellationToken);
        }

        // Fall back to the shared edition if this reader's personal one is empty
        // — better a generic digest than a blank home screen.
        if (digest is null && userId is not null)
        {
            digest = await LoadAsync(null, date, request.Period, cancellationToken);
        }

        if (digest is null)
        {
            return null;
        }

        if (userId is null || digest.Items.Count == 0)
        {
            return digest;
        }

        var ids = digest.Items.Select(i => i.Story.Id).ToList();
        var bookmarked = (await db.Bookmarks
                .AsNoTracking()
                .Where(b => b.UserId == userId && ids.Contains(b.StoryId))
                .Select(b => b.StoryId)
                .ToListAsync(cancellationToken))
            .ToHashSet();

        return digest with
        {
            Items = digest.Items
                .Select(i => bookmarked.Contains(i.Story.Id)
                    ? i with { Story = i.Story with { IsBookmarked = true } }
                    : i)
                .ToList()
        };
    }

    private async Task<DigestDto?> LoadAsync(
        Guid? userId,
        DateOnly date,
        DigestPeriod period,
        CancellationToken cancellationToken)
    {
        var card = StoryProjections.ToCard();

        return await db.Digests
            .AsNoTracking()
            .Where(d => d.UserId == userId && d.Date == date && d.Period == period)
            .Select(d => new DigestDto(
                d.Id,
                d.Date,
                d.Period,
                d.Intro,
                d.ReadingMinutes,
                d.GeneratedAt,
                d.Items
                    .OrderBy(i => i.Rank)
                    .Select(i => new DigestEntryDto(
                        i.Rank,
                        i.Reason,
                        db.Stories.Where(s => s.Id == i.StoryId).Select(card).First()))
                    .ToList()))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
