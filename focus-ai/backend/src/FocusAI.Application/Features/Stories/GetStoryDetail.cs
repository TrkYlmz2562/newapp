using FocusAI.Application.Common.Exceptions;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Common.Mappings;
using FocusAI.Application.Common.Services;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Enums;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Features.Stories;

public sealed record GetStoryDetailQuery(string Slug) : IRequest<StoryDetailDto>;

public sealed class GetStoryDetailQueryHandler(
    IApplicationDbContext db,
    ICurrentUser currentUser,
    IReaderContextFactory readerContextFactory,
    IVectorSearch vectorSearch) : IRequestHandler<GetStoryDetailQuery, StoryDetailDto>
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

        // Only published stories are servable. A Draft or Enriching story still
        // carries the raw article headline, no summary and — until enrichment
        // finishes — a slug that is allowed to change; the feed and search already
        // filter on Published, so serving one here just meant a direct link showed
        // a half-built card in the source language.
        if (story is null || story.Status != StoryStatus.Published)
        {
            throw NotFoundException.For("Haber", request.Slug);
        }

        var detail = StoryProjections.ToDetail(story);

        var userId = currentUser.UserId;
        var snapshot = await readerContextFactory.BuildAsync(userId, cancellationToken);

        var isBookmarked = userId is not null && await db.Bookmarks
            .AsNoTracking()
            .AnyAsync(b => b.UserId == userId && b.StoryId == story.Id, cancellationToken);

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

        return detail with
        {
            IsBookmarked = isBookmarked,
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
