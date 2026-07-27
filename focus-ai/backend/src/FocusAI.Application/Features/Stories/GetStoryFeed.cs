using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Application.Common.Models;
using FocusAI.Application.Common.Services;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Scoring;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Stories;

/// <summary>
/// How much of the feed the reader has not opened yet.
/// </summary>
/// <remarks>
/// Counted over the whole visible feed rather than the page on screen: the reader
/// asking this wants to know what is left, and a number that only described the
/// twenty cards they can already see would be answering a question nobody asked.
///
/// Deliberately not personalised. The ranker suppresses and reorders, and running
/// it to produce a count would cost a full ranking pass to answer a footnote —
/// and would then report a smaller feed than Keşfet visibly contains.
/// </remarks>
public sealed record GetUnreadCountQuery : IRequest<UnreadCountDto>;

public sealed class GetUnreadCountQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser) : IRequestHandler<GetUnreadCountQuery, UnreadCountDto>
{
    public async Task<UnreadCountDto> Handle(
        GetUnreadCountQuery request,
        CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            throw new UnauthorizedException();
        }

        var feed = db.Stories
            .AsNoTracking()
            .Where(s => s.Status == StoryStatus.Published)
            .Where(StoryFilters.VisibleToReaders);

        var total = await feed.CountAsync(cancellationToken);

        // Opening it is what "read" means, the same test the card badge and the
        // ranker apply. One NOT EXISTS rather than pulling the interaction rows
        // back: the reader may have thousands and we want a number.
        var unread = await feed.CountAsync(
            s => !db.Interactions.Any(i =>
                i.UserId == userId &&
                i.StoryId == s.Id &&
                (i.Type == InteractionType.Open || i.Type == InteractionType.ReadComplete)),
            cancellationToken);

        return new UnreadCountDto(unread, total);
    }
}

/// <summary>
/// The Home and Explore feeds. Personalised for signed-in readers, ranked purely
/// on global importance for everyone else.
/// </summary>
public sealed record GetStoryFeedQuery : IRequest<PagedResult<StoryCardDto>>
{
    public ContentCategory? Category { get; init; }

    public string? TopicSlug { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    /// <summary>Only stories published within this many hours. Null means no bound.</summary>
    public int? WithinHours { get; init; }

    /// <summary>Set false to see the shared editorial ordering while signed in.</summary>
    public bool Personalized { get; init; } = true;
}

public sealed class GetStoryFeedQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReaderContextFactory readerContextFactory,
    IDateTimeProvider clock) : IRequestHandler<GetStoryFeedQuery, PagedResult<StoryCardDto>>
{
    /// <summary>
    /// Candidate pool size for personalised ranking. Ranking happens in memory,
    /// so this bounds the cost; anything outside the top 300 by importance was
    /// never going to survive personalisation anyway.
    /// </summary>
    private const int CandidatePoolSize = 300;

    public async Task<PagedResult<StoryCardDto>> Handle(
        GetStoryFeedQuery request,
        CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 50);

        var baseQuery = db.Stories
            .AsNoTracking()
            .Where(s => s.Status == StoryStatus.Published)
            // Finance arrives here as one category among many, so it also has to
            // clear the commitment bar — see StoryFilters.
            .Where(StoryFilters.VisibleToReaders);

        if (request.Category is { } category)
        {
            baseQuery = baseQuery.Where(s => s.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(request.TopicSlug))
        {
            var slug = request.TopicSlug.Trim().ToLowerInvariant();
            baseQuery = baseQuery.Where(s => s.Topics.Any(t => t.Topic!.Slug == slug));
        }

        if (request.WithinHours is { } hours and > 0)
        {
            var cutoff = clock.UtcNow.AddHours(-hours);
            baseQuery = baseQuery.Where(s => s.PublishedAt >= cutoff);
        }

        var userId = currentUser.UserId;
        var shouldPersonalize = request.Personalized && userId is not null;

        if (!shouldPersonalize)
        {
            var result = await baseQuery
                .OrderByDescending(s => s.ImportanceScore)
                .ThenByDescending(s => s.PublishedAt)
                .Select(StoryProjections.ToCard())
                .ToPagedResultAsync(page, pageSize, cancellationToken);

            return await AttachReaderState(result, userId, cancellationToken);
        }

        var snapshot = await readerContextFactory.BuildAsync(userId, cancellationToken);

        var candidates = await baseQuery
            .OrderByDescending(s => s.ImportanceScore)
            .ThenByDescending(s => s.PublishedAt)
            .Take(CandidatePoolSize)
            .Select(s => new
            {
                s.Id,
                s.ImportanceScore,
                s.TrustScore,
                s.PublishedAt,
                s.Embedding,
                PrimarySourceId = s.Articles
                    .Where(a => a.IsPrimary)
                    .Select(a => a.SourceId)
                    .FirstOrDefault(),
                Topics = s.Topics.Select(t => new { t.TopicId, t.Weight }).ToList()
            })
            .ToListAsync(cancellationToken);

        var ranked = candidates
            .Select(c => PersonalizationScorer.Score(
                new RankableStory
                {
                    StoryId = c.Id,
                    ImportanceScore = c.ImportanceScore,
                    TrustScore = c.TrustScore,
                    PublishedAt = c.PublishedAt,
                    Embedding = c.Embedding,
                    PrimarySourceId = c.PrimarySourceId,
                    Topics = c.Topics.ToDictionary(t => t.TopicId, t => t.Weight)
                },
                snapshot.Context))
            .Where(r => !r.IsSuppressed)
            .OrderByDescending(r => r.Score)
            .ToList();

        var pageSlice = ranked
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var pageIds = pageSlice.Select(r => r.StoryId).ToList();

        var cards = await db.Stories
            .AsNoTracking()
            .Where(s => pageIds.Contains(s.Id))
            .Select(StoryProjections.ToCard())
            .ToListAsync(cancellationToken);

        // Re-apply the ranked order: the IN-list query returns rows in whatever
        // order Postgres finds convenient.
        var byId = cards.ToDictionary(c => c.Id);
        var ordered = pageSlice
            .Where(r => byId.ContainsKey(r.StoryId))
            .Select(r => byId[r.StoryId] with { Reason = r.Reason })
            .ToList();

        var paged = new PagedResult<StoryCardDto>
        {
            Items = ordered,
            Page = page,
            PageSize = pageSize,
            TotalCount = ranked.Count
        };

        return await AttachReaderState(paged, userId, cancellationToken);
    }

    /// <summary>
    /// Stamps the per-reader flags after projection. They cannot live in
    /// <see cref="StoryProjections.ToCard"/> because a `with` expression is
    /// illegal inside an expression tree.
    /// </summary>
    /// <remarks>
    /// Kept in step with <see cref="GetUnreadCountQueryHandler"/>: both call a
    /// story read on the same interaction types, because a badge saying "okundu"
    /// over a tally that still counts it would be worse than either being wrong.
    /// </remarks>
    private async Task<PagedResult<StoryCardDto>> AttachReaderState(
        PagedResult<StoryCardDto> result,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        if (userId is null || result.Items.Count == 0)
        {
            return result;
        }

        var ids = result.Items.Select(i => i.Id).ToList();

        var bookmarked = await db.Bookmarks
            .AsNoTracking()
            .Where(b => b.UserId == userId && ids.Contains(b.StoryId))
            .Select(b => b.StoryId)
            .ToListAsync(cancellationToken);

        // Opening the story is what "read" means here. It is also what the ranker
        // penalises, so the badge explains the demotion rather than contradicting it.
        var read = await db.Interactions
            .AsNoTracking()
            .Where(i => i.UserId == userId &&
                        ids.Contains(i.StoryId) &&
                        (i.Type == InteractionType.Open || i.Type == InteractionType.ReadComplete))
            .Select(i => i.StoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Newest verdict wins: interactions are append-only, so a reader who changed
        // their mind has both rows and only the last one is what they think now.
        var verdicts = await db.Interactions
            .AsNoTracking()
            .Where(i => i.UserId == userId &&
                        ids.Contains(i.StoryId) &&
                        (i.Type == InteractionType.Helpful || i.Type == InteractionType.NotHelpful))
            .OrderByDescending(i => i.OccurredAt)
            .Select(i => new { i.StoryId, i.Type })
            .ToListAsync(cancellationToken);

        if (bookmarked.Count == 0 && read.Count == 0 && verdicts.Count == 0)
        {
            return result;
        }

        var saved = bookmarked.ToHashSet();
        var seen = read.ToHashSet();
        var feedback = verdicts
            .GroupBy(v => v.StoryId)
            .ToDictionary(g => g.Key, g => g.First().Type);

        return result with
        {
            Items = result.Items
                .Select(i => i with
                {
                    IsBookmarked = i.IsBookmarked || saved.Contains(i.Id),
                    IsRead = seen.Contains(i.Id),
                    Feedback = feedback.TryGetValue(i.Id, out var verdict) ? verdict : null
                })
                .ToList()
        };
    }
}
