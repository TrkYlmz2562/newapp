using FocusAI.Domain.Text;

namespace FocusAI.Domain.Scoring;

/// <summary>Story-side inputs to personalised ranking.</summary>
public sealed record RankableStory
{
    public required Guid StoryId { get; init; }

    public required int ImportanceScore { get; init; }

    public required int TrustScore { get; init; }

    /// <summary>Topic id → tagger confidence in [0,1].</summary>
    public IReadOnlyDictionary<Guid, double> Topics { get; init; } = new Dictionary<Guid, double>();

    public Guid PrimarySourceId { get; init; }

    public float[]? Embedding { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }
}

/// <summary>Reader-side inputs. Everything is optional — a brand-new user still ranks.</summary>
public sealed record ReaderContext
{
    /// <summary>Topic id → interest weight in [0,1].</summary>
    public IReadOnlyDictionary<Guid, double> Interests { get; init; } = new Dictionary<Guid, double>();

    public IReadOnlySet<Guid> MutedTopicIds { get; init; } = new HashSet<Guid>();

    public IReadOnlySet<Guid> FavoriteSourceIds { get; init; } = new HashSet<Guid>();

    /// <summary>Stories already opened. Demoted, never removed — see SeenPenalty.</summary>
    public IReadOnlySet<Guid> SeenStoryIds { get; init; } = new HashSet<Guid>();

    /// <summary>Stories the reader explicitly asked to see less of.</summary>
    public IReadOnlySet<Guid> DownrankedStoryIds { get; init; } = new HashSet<Guid>();

    /// <summary>
    /// Topic id → net feedback in [-1,1], from the reader pressing "faydalı" or
    /// "az göster" on stories carrying that topic. Distinct from Interests, which
    /// the reader declared once at onboarding: this is what they keep telling us.
    /// </summary>
    public IReadOnlyDictionary<Guid, double> TopicFeedback { get; init; } = new Dictionary<Guid, double>();

    /// <summary>Centroid of recently-read stories. Catches interests the user never declared.</summary>
    public float[]? TasteVector { get; init; }
}

public sealed record RankedStory(Guid StoryId, double Score, string Reason, bool IsSuppressed);

/// <summary>
/// "Bana Özel" ranking from PRD section 5.6. Mutes are a hard filter; everything
/// else is a weighted blend of global importance and personal fit, so an
/// objectively huge story still surfaces for someone who never declared its topic.
/// </summary>
public static class PersonalizationScorer
{
    private const double ImportanceWeight = 0.45;
    private const double InterestWeight = 0.35;
    private const double TasteWeight = 0.12;
    private const double SourceAffinityWeight = 0.08;

    /// <summary>Multiplier applied to stories the reader already opened.</summary>
    private const double SeenPenalty = 0.25;

    /// <summary>
    /// Multiplier for a story the reader pressed "az göster" on. Deliberately a
    /// heavy demotion rather than a hard filter: the reader asked for less of
    /// this, not for it to be censored, and a story big enough can still surface.
    /// </summary>
    private const double DownrankPenalty = 0.15;

    /// <summary>
    /// How far accumulated topic feedback can move a score, either way. Bounded so
    /// a handful of taps cannot bury a genuinely major story, and so an early
    /// mistake stays recoverable by tapping the other way.
    /// </summary>
    private const double FeedbackSwing = 0.35;

    public static RankedStory Score(RankableStory story, ReaderContext reader)
    {
        if (story.Topics.Keys.Any(reader.MutedTopicIds.Contains))
        {
            return new RankedStory(story.StoryId, 0d, "Sessize alınan konu.", IsSuppressed: true);
        }

        var interest = InterestMatch(story, reader, out var topInterestWeight);
        var taste = Math.Max(0d, VectorMath.CosineSimilarity(story.Embedding, reader.TasteVector)) * 100;
        var sourceAffinity = reader.FavoriteSourceIds.Contains(story.PrimarySourceId) ? 100d : 0d;

        var score =
            story.ImportanceScore * ImportanceWeight +
            interest * InterestWeight +
            taste * TasteWeight +
            sourceAffinity * SourceAffinityWeight;

        var feedback = TopicFeedbackSignal(story, reader);
        if (feedback != 0d)
        {
            score *= 1d + feedback * FeedbackSwing;
        }

        if (reader.SeenStoryIds.Contains(story.StoryId))
        {
            score *= SeenPenalty;
        }

        if (reader.DownrankedStoryIds.Contains(story.StoryId))
        {
            score *= DownrankPenalty;
        }

        return new RankedStory(
            story.StoryId,
            Math.Round(Math.Clamp(score, 0d, 100d), 4),
            BuildReason(story, interest, topInterestWeight, sourceAffinity > 0, feedback),
            IsSuppressed: false);
    }

    public static IReadOnlyList<RankedStory> Rank(
        IEnumerable<RankableStory> stories,
        ReaderContext reader,
        int take = 10)
    {
        return stories
            .Select(story => Score(story, reader))
            .Where(result => !result.IsSuppressed)
            .OrderByDescending(result => result.Score)
            .ThenByDescending(result => result.StoryId)
            .Take(take)
            .ToList();
    }

    /// <summary>
    /// Mean feedback across the topics this story carries, so a story touching one
    /// liked topic and one disliked topic lands near neutral rather than inheriting
    /// whichever the reader pressed most recently.
    /// </summary>
    private static double TopicFeedbackSignal(RankableStory story, ReaderContext reader)
    {
        if (reader.TopicFeedback.Count == 0 || story.Topics.Count == 0)
        {
            return 0d;
        }

        var total = 0d;
        var matched = 0;

        foreach (var topicId in story.Topics.Keys)
        {
            if (!reader.TopicFeedback.TryGetValue(topicId, out var signal))
            {
                continue;
            }

            total += signal;
            matched++;
        }

        return matched == 0 ? 0d : Math.Clamp(total / matched, -1d, 1d);
    }

    /// <summary>
    /// Best-matching declared interest, softened by how many of the reader's
    /// interests the story touches. Using the max rather than the mean keeps a
    /// story that nails one declared interest from being diluted by the others.
    /// </summary>
    private static double InterestMatch(RankableStory story, ReaderContext reader, out double topWeight)
    {
        topWeight = 0d;

        if (reader.Interests.Count == 0 || story.Topics.Count == 0)
        {
            return 0d;
        }

        double best = 0d;
        var matches = 0;

        foreach (var (topicId, tagConfidence) in story.Topics)
        {
            if (!reader.Interests.TryGetValue(topicId, out var interestWeight))
            {
                continue;
            }

            matches++;
            var combined = Math.Clamp(interestWeight, 0d, 1d) * Math.Clamp(tagConfidence, 0d, 1d);
            if (combined > best)
            {
                best = combined;
                topWeight = interestWeight;
            }
        }

        if (matches == 0)
        {
            return 0d;
        }

        // Each additional matching interest adds a small, saturating bonus.
        var breadthBonus = Math.Min(0.2d, (matches - 1) * 0.07d);
        return Math.Min(1d, best + breadthBonus) * 100;
    }

    private static string BuildReason(
        RankableStory story,
        double interest,
        double topInterestWeight,
        bool favoriteSource,
        double feedback)
    {
        // Named before the other reasons: when the reader's own taps moved a story,
        // that is the honest explanation for where it landed.
        if (feedback >= 0.5)
        {
            return "Bu konuya faydalı dedin.";
        }

        if (feedback <= -0.5)
        {
            return "Bu konudan daha az istemiştin.";
        }

        if (interest >= 60 && topInterestWeight > 0)
        {
            return "İlgi alanlarınla doğrudan ilgili.";
        }

        if (favoriteSource)
        {
            return "Takip ettiğin kaynaktan.";
        }

        if (story.ImportanceScore >= 85)
        {
            return "Bugünün en büyük gelişmelerinden.";
        }

        if (story.TrustScore >= 85)
        {
            return "Birden fazla resmi kaynak doğruladı.";
        }

        return interest > 0 ? "İlgi alanlarınla kısmen örtüşüyor." : "Alanında öne çıkan gelişme.";
    }
}
