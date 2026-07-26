using FocusAI.Domain.Scoring;
using Xunit;

namespace FocusAI.UnitTests.Scoring;

public class PersonalizationScorerTests
{
    private static readonly Guid DotNet = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Angular = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Crypto = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid SourceA = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static RankableStory Story(
        Guid id,
        int importance = 50,
        int trust = 60,
        Guid? topic = null,
        Guid? sourceId = null) =>
        new()
        {
            StoryId = id,
            ImportanceScore = importance,
            TrustScore = trust,
            PublishedAt = Now,
            PrimarySourceId = sourceId ?? SourceA,
            Topics = topic is { } t
                ? new Dictionary<Guid, double> { [t] = 1.0 }
                : new Dictionary<Guid, double>()
        };

    private static ReaderContext Reader() => new()
    {
        Interests = new Dictionary<Guid, double> { [DotNet] = 1.0, [Angular] = 0.8 },
        MutedTopicIds = new HashSet<Guid> { Crypto }
    };

    [Fact]
    public void Muted_topics_are_suppressed_outright()
    {
        var result = PersonalizationScorer.Score(
            Story(Guid.NewGuid(), importance: 100, topic: Crypto),
            Reader());

        // A mute is a hard filter, not a demotion — even a perfect-importance
        // story must not reappear.
        Assert.True(result.IsSuppressed);
        Assert.Equal(0d, result.Score);
    }

    [Fact]
    public void Muted_story_is_dropped_from_ranked_output()
    {
        var stories = new[]
        {
            Story(Guid.NewGuid(), importance: 100, topic: Crypto),
            Story(Guid.NewGuid(), importance: 10, topic: DotNet)
        };

        var ranked = PersonalizationScorer.Rank(stories, Reader());

        Assert.Single(ranked);
        Assert.Equal(stories[1].StoryId, ranked[0].StoryId);
    }

    [Fact]
    public void Declared_interest_beats_equal_importance_without_one()
    {
        var matching = PersonalizationScorer.Score(
            Story(Guid.NewGuid(), importance: 50, topic: DotNet), Reader());

        var unrelated = PersonalizationScorer.Score(
            Story(Guid.NewGuid(), importance: 50), Reader());

        Assert.True(matching.Score > unrelated.Score);
        Assert.Equal("İlgi alanlarınla doğrudan ilgili.", matching.Reason);
    }

    [Fact]
    public void Major_story_still_surfaces_for_an_uninterested_reader()
    {
        // Personalisation must not become a filter bubble: importance carries
        // the largest single weight for exactly this reason.
        var huge = PersonalizationScorer.Score(
            Story(Guid.NewGuid(), importance: 100), Reader());

        var smallButRelevant = PersonalizationScorer.Score(
            Story(Guid.NewGuid(), importance: 20, topic: DotNet), Reader());

        Assert.True(huge.Score > smallButRelevant.Score);
    }

    [Fact]
    public void Already_seen_stories_are_demoted_not_removed()
    {
        var storyId = Guid.NewGuid();
        var reader = Reader() with { SeenStoryIds = new HashSet<Guid> { storyId } };

        var seen = PersonalizationScorer.Score(Story(storyId, importance: 80, topic: DotNet), reader);
        var unseen = PersonalizationScorer.Score(
            Story(Guid.NewGuid(), importance: 80, topic: DotNet), reader);

        Assert.False(seen.IsSuppressed);
        Assert.True(seen.Score < unseen.Score);
    }

    [Fact]
    public void Favorite_source_lifts_an_otherwise_unremarkable_story()
    {
        var favorite = Guid.NewGuid();
        var reader = Reader() with { FavoriteSourceIds = new HashSet<Guid> { favorite } };

        var fromFavorite = PersonalizationScorer.Score(
            Story(Guid.NewGuid(), importance: 40, sourceId: favorite), reader);

        var fromOther = PersonalizationScorer.Score(
            Story(Guid.NewGuid(), importance: 40), reader);

        Assert.True(fromFavorite.Score > fromOther.Score);
    }

    [Fact]
    public void Anonymous_reader_falls_back_to_pure_importance_order()
    {
        var anonymous = new ReaderContext();

        var high = PersonalizationScorer.Score(Story(Guid.NewGuid(), importance: 90), anonymous);
        var low = PersonalizationScorer.Score(Story(Guid.NewGuid(), importance: 30), anonymous);

        Assert.True(high.Score > low.Score);
        Assert.False(high.IsSuppressed);
    }

    [Fact]
    public void Downranked_story_is_demoted_but_never_removed()
    {
        var id = Guid.NewGuid();
        var reader = Reader() with { DownrankedStoryIds = new HashSet<Guid> { id } };

        var result = PersonalizationScorer.Score(Story(id, importance: 90), reader);

        // "Az göster" is not a mute: the reader asked for less of this, not for it
        // to be hidden from them, so the story stays rankable.
        Assert.False(result.IsSuppressed);
        Assert.True(result.Score > 0d);
        Assert.True(result.Score < PersonalizationScorer.Score(Story(id, importance: 90), Reader()).Score);
    }

    [Fact]
    public void Positive_topic_feedback_lifts_and_negative_lowers()
    {
        var liked = Reader() with { TopicFeedback = new Dictionary<Guid, double> { [Angular] = 1.0 } };
        var disliked = Reader() with { TopicFeedback = new Dictionary<Guid, double> { [Angular] = -1.0 } };

        var up = PersonalizationScorer.Score(Story(Guid.NewGuid(), topic: Angular), liked);
        var neutral = PersonalizationScorer.Score(Story(Guid.NewGuid(), topic: Angular), Reader());
        var down = PersonalizationScorer.Score(Story(Guid.NewGuid(), topic: Angular), disliked);

        Assert.True(up.Score > neutral.Score);
        Assert.True(down.Score < neutral.Score);
    }

    [Fact]
    public void Topic_feedback_cannot_bury_a_major_story()
    {
        var reader = Reader() with { TopicFeedback = new Dictionary<Guid, double> { [Angular] = -1.0 } };

        var major = PersonalizationScorer.Score(Story(Guid.NewGuid(), importance: 100, topic: Angular), reader);
        var minor = PersonalizationScorer.Score(Story(Guid.NewGuid(), importance: 20, topic: Angular), Reader());

        // The swing is bounded, so a few taps tilt the feed without letting the
        // reader accidentally opt out of the day's biggest development.
        Assert.True(major.Score > minor.Score);
    }

    [Fact]
    public void Mixed_feedback_across_a_story_topics_lands_near_neutral()
    {
        var reader = Reader() with
        {
            TopicFeedback = new Dictionary<Guid, double> { [DotNet] = 1.0, [Angular] = -1.0 }
        };

        var mixed = new RankableStory
        {
            StoryId = Guid.NewGuid(),
            ImportanceScore = 50,
            TrustScore = 60,
            PublishedAt = Now,
            PrimarySourceId = SourceA,
            Topics = new Dictionary<Guid, double> { [DotNet] = 1.0, [Angular] = 1.0 }
        };

        var withFeedback = PersonalizationScorer.Score(mixed, reader);
        var withoutFeedback = PersonalizationScorer.Score(mixed, Reader());

        // The mean, not the loudest tap: a story touching a liked and a disliked
        // topic should not inherit whichever the reader pressed most recently.
        Assert.Equal(withoutFeedback.Score, withFeedback.Score, 4);
    }

    [Fact]
    public void Feedback_on_an_untouched_topic_changes_nothing()
    {
        var reader = Reader() with { TopicFeedback = new Dictionary<Guid, double> { [Crypto] = 1.0 } };

        var story = Story(Guid.NewGuid(), topic: Angular);

        Assert.Equal(
            PersonalizationScorer.Score(story, Reader()).Score,
            PersonalizationScorer.Score(story, reader).Score,
            4);
    }

    [Fact]
    public void Feedback_is_named_as_the_reason_when_it_moved_the_story()
    {
        var liked = Reader() with { TopicFeedback = new Dictionary<Guid, double> { [Angular] = 1.0 } };
        var disliked = Reader() with { TopicFeedback = new Dictionary<Guid, double> { [Angular] = -1.0 } };

        // The reader's own tap is the honest explanation for where a story landed.
        Assert.Equal(
            "Bu konuya faydalı dedin.",
            PersonalizationScorer.Score(Story(Guid.NewGuid(), topic: Angular), liked).Reason);

        Assert.Equal(
            "Bu konudan daha az istemiştin.",
            PersonalizationScorer.Score(Story(Guid.NewGuid(), topic: Angular), disliked).Reason);
    }

    [Fact]
    public void Rank_respects_the_take_limit_and_returns_descending_scores()
    {
        var stories = Enumerable.Range(1, 30)
            .Select(i => Story(Guid.NewGuid(), importance: i * 3))
            .ToList();

        var ranked = PersonalizationScorer.Rank(stories, Reader(), take: 10);

        Assert.Equal(10, ranked.Count);
        Assert.Equal(ranked.OrderByDescending(r => r.Score).Select(r => r.StoryId), ranked.Select(r => r.StoryId));
    }
}
