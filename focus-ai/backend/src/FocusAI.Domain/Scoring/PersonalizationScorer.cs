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

    /// <summary>Stories already opened; suppressed from the feed but not from search.</summary>
    public IReadOnlySet<Guid> SeenStoryIds { get; init; } = new HashSet<Guid>();

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

        if (reader.SeenStoryIds.Contains(story.StoryId))
        {
            score *= SeenPenalty;
        }

        return new RankedStory(
            story.StoryId,
            Math.Round(Math.Clamp(score, 0d, 100d), 4),
            BuildReason(story, interest, topInterestWeight, sourceAffinity > 0),
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
        bool favoriteSource)
    {
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
