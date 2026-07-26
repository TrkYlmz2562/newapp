using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Scoring;
using FocusAI.Domain.Text;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Common.Services;

/// <summary>Reader state assembled once and reused across ranking calls in a request.</summary>
public sealed record ReaderSnapshot(
    ReaderContext Context,
    IReadOnlyList<string> InterestSlugs,
    string Language,
    string TimeZone,
    int DailyStoryCount);

public interface IReaderContextFactory
{
    Task<ReaderSnapshot> BuildAsync(Guid? userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Loads interests, mutes, favourites, recently-seen stories and the behavioural
/// taste vector that <see cref="PersonalizationScorer"/> needs. Anonymous callers
/// get a neutral snapshot so every ranked endpoint works logged out.
/// </summary>
public sealed class ReaderContextFactory(IApplicationDbContext db) : IReaderContextFactory
{
    /// <summary>How many recent opens feed the taste vector. Short window keeps it responsive.</summary>
    private const int TasteWindowSize = 40;

    private static readonly ReaderSnapshot Anonymous = new(
        new ReaderContext(),
        [],
        "tr",
        "Europe/Istanbul",
        10);

    public async Task<ReaderSnapshot> BuildAsync(Guid? userId, CancellationToken cancellationToken = default)
    {
        if (userId is not { } id)
        {
            return Anonymous;
        }

        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Locale,
                u.TimeZone,
                DailyStoryCount = u.Profile != null ? u.Profile.DailyStoryCount : 10
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
        {
            return Anonymous;
        }

        var interests = await db.UserInterests
            .AsNoTracking()
            .Where(i => i.UserProfile!.UserId == id)
            .Select(i => new { i.TopicId, i.Weight, Slug = i.Topic!.Slug })
            .ToListAsync(cancellationToken);

        var muted = await db.UserMutedTopics
            .AsNoTracking()
            .Where(m => m.UserProfile!.UserId == id)
            .Select(m => m.TopicId)
            .ToListAsync(cancellationToken);

        var favorites = await db.UserFavoriteSources
            .AsNoTracking()
            .Where(f => f.UserProfile!.UserId == id)
            .Select(f => f.SourceId)
            .ToListAsync(cancellationToken);

        var recentOpens = await db.Interactions
            .AsNoTracking()
            .Where(i => i.UserId == id &&
                        (i.Type == InteractionType.Open || i.Type == InteractionType.ReadComplete))
            .OrderByDescending(i => i.OccurredAt)
            .Take(TasteWindowSize)
            .Select(i => i.StoryId)
            .ToListAsync(cancellationToken);

        var seen = await db.Interactions
            .AsNoTracking()
            .Where(i => i.UserId == id && i.Type != InteractionType.Impression)
            .Select(i => i.StoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var embeddings = await db.Stories
            .AsNoTracking()
            .Where(s => recentOpens.Contains(s.Id) && s.Embedding != null)
            .Select(s => s.Embedding!)
            .ToListAsync(cancellationToken);

        var context = new ReaderContext
        {
            Interests = interests.ToDictionary(i => i.TopicId, i => i.Weight),
            MutedTopicIds = muted.ToHashSet(),
            FavoriteSourceIds = favorites.ToHashSet(),
            SeenStoryIds = seen.ToHashSet(),
            TasteVector = VectorMath.Centroid(embeddings)
        };

        return new ReaderSnapshot(
            context,
            interests.Select(i => i.Slug).ToList(),
            user.Locale,
            user.TimeZone,
            user.DailyStoryCount);
    }
}
