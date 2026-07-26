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

    /// <summary>
    /// How many explicit taps are considered. Deliberately much wider than the taste
    /// window: an opinion the reader stated by hand should outlive forty page opens.
    /// </summary>
    private const int FeedbackWindowSize = 200;

    /// <summary>
    /// Net taps on one topic needed for a full-strength signal. Three, so a single
    /// mistap barely registers while a consistent opinion arrives quickly.
    /// </summary>
    private const int FeedbackSaturation = 3;

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

        // Rating a story is an opinion, not consumption: a reader who taps "faydalı"
        // from the feed has not read it yet, and demoting it as seen would make the
        // story vanish as a reward for liking it. "Az göster" carries its own,
        // heavier penalty, so it does not need this one either.
        var seen = await db.Interactions
            .AsNoTracking()
            .Where(i => i.UserId == id &&
                        i.Type != InteractionType.Impression &&
                        i.Type != InteractionType.Helpful &&
                        i.Type != InteractionType.NotHelpful)
            .Select(i => i.StoryId)
            .Distinct()
            .ToListAsync(cancellationToken);

        // Explicit feedback: what the reader keeps telling us, as opposed to the
        // interests they declared once during onboarding.
        var feedbackRows = await db.Interactions
            .AsNoTracking()
            .Where(i => i.UserId == id &&
                        (i.Type == InteractionType.Helpful || i.Type == InteractionType.NotHelpful))
            .OrderByDescending(i => i.OccurredAt)
            .Take(FeedbackWindowSize)
            .Select(i => new { i.StoryId, i.Type })
            .ToListAsync(cancellationToken);

        // Interactions are append-only, so one story can carry several rows — a reader
        // who changed their mind, or double-tapped. Only the newest verdict per story
        // counts; otherwise repeated taps on a single story would saturate its topics.
        var feedback = feedbackRows
            .GroupBy(f => f.StoryId)
            .Select(g => g.First())
            .ToList();

        var downranked = feedback
            .Where(f => f.Type == InteractionType.NotHelpful)
            .Select(f => f.StoryId)
            .ToHashSet();

        var topicFeedback = new Dictionary<Guid, double>();

        if (feedback.Count > 0)
        {
            var feedbackStoryIds = feedback.Select(f => f.StoryId).ToList();

            var storyTopics = await db.Stories
                .AsNoTracking()
                .Where(s => feedbackStoryIds.Contains(s.Id))
                .SelectMany(s => s.Topics.Select(st => new { st.StoryId, st.TopicId }))
                .ToListAsync(cancellationToken);

            var byStory = storyTopics
                .GroupBy(st => st.StoryId)
                .ToDictionary(g => g.Key, g => g.Select(st => st.TopicId).ToList());

            var tally = new Dictionary<Guid, int>();

            foreach (var entry in feedback)
            {
                if (!byStory.TryGetValue(entry.StoryId, out var topicIds))
                {
                    continue;
                }

                var delta = entry.Type == InteractionType.Helpful ? 1 : -1;
                foreach (var topicId in topicIds)
                {
                    tally[topicId] = tally.GetValueOrDefault(topicId) + delta;
                }
            }

            // Normalised so a topic needs repeated agreement to reach full strength;
            // one tap should nudge the feed, not redefine it.
            foreach (var (topicId, net) in tally)
            {
                topicFeedback[topicId] = Math.Clamp(net / (double)FeedbackSaturation, -1d, 1d);
            }
        }

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
            DownrankedStoryIds = downranked,
            TopicFeedback = topicFeedback,
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
