using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Application.Common.Services;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Timeline;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Stories;

public sealed record GetStoryDetailQuery(string Slug) : IRequest<StoryDetailDto>;

public sealed class GetStoryDetailQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReaderContextFactory readerContextFactory,
    IVectorSearch vectorSearch,
    IDateTimeProvider clock) : IRequestHandler<GetStoryDetailQuery, StoryDetailDto>
{
    private const int RelatedCount = 5;

    public async Task<StoryDetailDto> Handle(GetStoryDetailQuery request, CancellationToken cancellationToken)
    {
        var slug = request.Slug.Trim().ToLowerInvariant();

        var story = await db.Stories
            .AsNoTracking()
            .Include(s => s.Summary)
            .Include(s => s.Analysis)
            .Include(s => s.Trust)
            .Include(s => s.Comparison)
            .Include(s => s.Links)
            .Include(s => s.Topics).ThenInclude(t => t.Topic)
            .Include(s => s.Articles).ThenInclude(a => a.Source)
            .FirstOrDefaultAsync(s => s.Slug == slug, cancellationToken);

        // A story that has never been published is not servable: it still carries the
        // raw article headline, has no summary, and its slug is allowed to change, so
        // a direct link showed a half-built card in the source language.
        //
        // The test is FirstPublishedAt rather than Status. Clustering demotes a
        // Published story back to Enriching whenever new coverage arrives, so keying
        // off Status would make a live story 404 for the length of its re-enrichment
        // — and permanently if enrichment kept failing. Once published, a story stays
        // reachable; only Suppressed takes it back off the site.
        if (story is null ||
            story.Status == StoryStatus.Suppressed ||
            story.FirstPublishedAt is null)
        {
            throw NotFoundException.For("Haber", request.Slug);
        }

        var detail = StoryProjections.ToDetail(story);

        var userId = currentUser.UserId;
        var snapshot = await readerContextFactory.BuildAsync(userId, cancellationToken);

        var isBookmarked = userId is not null && await db.Bookmarks
            .AsNoTracking()
            .AnyAsync(b => b.UserId == userId && b.StoryId == story.Id, cancellationToken);

        // Unfinished only: having studied this once does not make the control on the
        // page read as already-taken, because wanting to study it again is a new
        // lesson — the same rule QueueStoryBriefCommand applies.
        var learningBriefId = userId is null
            ? null
            : await db.LearningBriefs
                .AsNoTracking()
                .Where(b => b.UserId == userId &&
                            b.Status != BriefStatus.Done &&
                            b.Stories.Any(s => s.StoryId == story.Id))
                .Select(b => (Guid?)b.Id)
                .FirstOrDefaultAsync(cancellationToken);

        // Newest verdict wins — interactions are append-only, so a changed mind
        // leaves both rows behind.
        var feedback = userId is null
            ? null
            : await db.Interactions
                .AsNoTracking()
                .Where(i => i.UserId == userId &&
                            i.StoryId == story.Id &&
                            (i.Type == InteractionType.Helpful || i.Type == InteractionType.NotHelpful))
                .OrderByDescending(i => i.OccurredAt)
                .Select(i => (InteractionType?)i.Type)
                .FirstOrDefaultAsync(cancellationToken);

        var personalNote = story.Analysis is null
            ? null
            : StoryProjections.PickPersonalNote(story.Analysis.StackNotes, snapshot.InterestSlugs);

        var related = await LoadRelatedAsync(story.Id, story.Embedding, story.Topics.Select(t => t.TopicId).ToList(), cancellationToken);

        // Derived here rather than in the projection: it needs the member articles
        // as objects, and it is pure arithmetic over timestamps — no query, no cost.
        var timeline = StoryTimeline.Build(
            story.Articles
                .Select(a => new TimelineEntry(
                    a.PublishedAt,
                    a.Source?.Name ?? "Bilinmeyen kaynak",
                    a.Source?.IsOfficial ?? false))
                .ToList(),
            clock.UtcNow);

        return detail with
        {
            IsBookmarked = isBookmarked,
            LearningBriefId = learningBriefId,
            Timeline = timeline is null
                ? null
                : new TimelineDto(
                    timeline.Phase,
                    timeline.FirstAt,
                    timeline.LatestAt,
                    timeline.OutletCount,
                    timeline.SpanHours,
                    timeline.LongestQuietHours,
                    timeline.Moments
                        .Select(m => new TimelineMomentDto(m.Kind, m.At, m.SourceName))
                        .ToList()),
            Feedback = feedback,
            PersonalNote = personalNote,
            Related = related
        };
    }

    /// <summary>
    /// Prefers embedding similarity; falls back to shared-topic overlap when the
    /// story has no vector yet (or the vector index is unavailable), so the
    /// "İlgili Haberler" block is never empty on a freshly ingested story.
    /// </summary>
    private async Task<IReadOnlyList<StoryCardDto>> LoadRelatedAsync(
        Guid storyId,
        float[]? embedding,
        IReadOnlyCollection<Guid> topicIds,
        CancellationToken cancellationToken)
    {
        if (embedding is { Length: > 0 })
        {
            var hits = await vectorSearch.FindSimilarStoriesAsync(
                embedding, RelatedCount, storyId, cancellationToken);

            if (hits.Count > 0)
            {
                var hitIds = hits.Select(h => h.StoryId).ToList();
                var cards = await db.Stories
                    .AsNoTracking()
                    .Where(s => hitIds.Contains(s.Id) && s.Status == StoryStatus.Published)
                    .Select(StoryProjections.ToCard())
                    .ToListAsync(cancellationToken);

                var order = hitIds.Select((id, index) => (id, index)).ToDictionary(x => x.id, x => x.index);
                return cards.OrderBy(c => order.TryGetValue(c.Id, out var i) ? i : int.MaxValue).ToList();
            }
        }

        if (topicIds.Count == 0)
        {
            return [];
        }

        return await db.Stories
            .AsNoTracking()
            .Where(s => s.Id != storyId &&
                        s.Status == StoryStatus.Published &&
                        s.Topics.Any(t => topicIds.Contains(t.TopicId)))
            .OrderByDescending(s => s.Topics.Count(t => topicIds.Contains(t.TopicId)))
            .ThenByDescending(s => s.ImportanceScore)
            .Take(RelatedCount)
            .Select(StoryProjections.ToCard())
            .ToListAsync(cancellationToken);
    }
}

/// <summary>Powers the "🔥 Günün En Önemlisi" card on the home screen.</summary>
public sealed record GetTopStoryQuery(int WithinHours = 24) : IRequest<StoryCardDto?>;

public sealed class GetTopStoryQueryHandler(
    IApplicationDbContext db,
    IDateTimeProvider clock) : IRequestHandler<GetTopStoryQuery, StoryCardDto?>
{
    public async Task<StoryCardDto?> Handle(GetTopStoryQuery request, CancellationToken cancellationToken)
    {
        var cutoff = clock.UtcNow.AddHours(-Math.Max(1, request.WithinHours));

        var top = await db.Stories
            .AsNoTracking()
            .Where(s => s.Status == StoryStatus.Published && s.PublishedAt >= cutoff)
            .OrderByDescending(s => s.ImportanceScore)
            .ThenByDescending(s => s.TrustScore)
            .Select(StoryProjections.ToCard())
            .FirstOrDefaultAsync(cancellationToken);

        // A quiet night should still show something rather than an empty hero slot.
        return top ?? await db.Stories
            .AsNoTracking()
            .Where(s => s.Status == StoryStatus.Published)
            .OrderByDescending(s => s.PublishedAt)
            .Select(StoryProjections.ToCard())
            .FirstOrDefaultAsync(cancellationToken);
    }
}
