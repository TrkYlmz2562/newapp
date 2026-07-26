using FocusAI.Domain.Enums;
using FocusAI.Domain.Scoring;
using Xunit;

namespace FocusAI.UnitTests.Scoring;

public class DigestComposerTests
{
    private static DigestCandidate Candidate(double score, ContentCategory category, int minutes = 1) =>
        new(Guid.NewGuid(), score, category, minutes, "test");

    [Fact]
    public void Returns_at_most_the_requested_count()
    {
        var candidates = Enumerable.Range(1, 50)
            .Select(i => Candidate(i, ContentCategory.Ai))
            .ToList();

        Assert.Equal(10, DigestComposer.Compose(candidates, take: 10).Count);
    }

    [Fact]
    public void Highest_scores_come_first()
    {
        var candidates = new[]
        {
            Candidate(10, ContentCategory.Ai),
            Candidate(90, ContentCategory.Software),
            Candidate(50, ContentCategory.Tools)
        };

        var composed = DigestComposer.Compose(candidates, take: 3);

        Assert.Equal([90d, 50d, 10d], composed.Select(c => c.Score));
    }

    [Fact]
    public void One_category_cannot_monopolize_the_edition()
    {
        // Twenty AI stories and a handful of others: without the cap, a big model
        // release day would push every other category off the digest entirely.
        var candidates = Enumerable.Range(1, 20)
            .Select(i => Candidate(100 - i, ContentCategory.Ai))
            .Concat(
            [
                Candidate(10, ContentCategory.Software),
                Candidate(9, ContentCategory.OpenSource),
                Candidate(8, ContentCategory.Career)
            ])
            .ToList();

        var composed = DigestComposer.Compose(candidates, take: 6, maxPerCategory: 4);

        Assert.Equal(4, composed.Count(c => c.Category == ContentCategory.Ai));
        Assert.Equal(2, composed.Count(c => c.Category != ContentCategory.Ai));
    }

    [Fact]
    public void Edition_is_backfilled_from_overflow_on_a_quiet_day()
    {
        // Only AI stories exist today. A short digest would be worse than one
        // that exceeds the per-category cap, so overflow backfills the slots.
        var candidates = Enumerable.Range(1, 10)
            .Select(i => Candidate(100 - i, ContentCategory.Ai))
            .ToList();

        var composed = DigestComposer.Compose(candidates, take: 8, maxPerCategory: 3);

        Assert.Equal(8, composed.Count);
    }

    [Fact]
    public void Backfill_preserves_score_order_within_the_overflow()
    {
        var candidates = Enumerable.Range(1, 6)
            .Select(i => Candidate(100 - i, ContentCategory.Ai))
            .ToList();

        var composed = DigestComposer.Compose(candidates, take: 6, maxPerCategory: 2);

        Assert.Equal(candidates.OrderByDescending(c => c.Score).Select(c => c.Score),
            composed.Select(c => c.Score));
    }

    [Fact]
    public void Empty_input_yields_an_empty_edition() =>
        Assert.Empty(DigestComposer.Compose([], take: 10));

    [Fact]
    public void Reading_time_sums_members_and_never_reports_zero()
    {
        var composed = new[]
        {
            Candidate(10, ContentCategory.Ai, minutes: 2),
            Candidate(9, ContentCategory.Software, minutes: 3)
        };

        Assert.Equal(5, DigestComposer.EstimateReadingMinutes(composed));
        Assert.Equal(1, DigestComposer.EstimateReadingMinutes([]));
    }

    [Fact]
    public void Composition_is_deterministic_for_tied_scores()
    {
        var candidates = Enumerable.Range(1, 12)
            .Select(_ => Candidate(50, ContentCategory.Ai))
            .ToList();

        var first = DigestComposer.Compose(candidates, take: 5).Select(c => c.StoryId);
        var second = DigestComposer.Compose(candidates, take: 5).Select(c => c.StoryId);

        Assert.Equal(first, second);
    }
}

public class ImportanceScoreCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static ImportanceScoreInput Input(
        int trust = 60,
        int distinctSources = 1,
        int officialSources = 0,
        int engagement = 0,
        int ageHours = 2,
        double? llmImportance = null,
        ContentCategory category = ContentCategory.Software) =>
        new()
        {
            TrustScore = trust,
            DistinctSourceCount = distinctSources,
            OfficialSourceCount = officialSources,
            EngagementScore = engagement,
            PublishedAt = Now.AddHours(-ageHours),
            Now = Now,
            LlmImportance = llmImportance,
            Category = category
        };

    [Fact]
    public void Score_stays_within_bounds()
    {
        var min = ImportanceScoreCalculator.Calculate(Input(
            trust: 0, distinctSources: 0, ageHours: 10_000, llmImportance: 0));
        var max = ImportanceScoreCalculator.Calculate(Input(
            trust: 100, distinctSources: 40, officialSources: 10, engagement: 100_000,
            ageHours: 0, llmImportance: 1, category: ContentCategory.Ai));

        Assert.InRange(min, 0, 100);
        Assert.InRange(max, 0, 100);
        Assert.True(max > min);
    }

    [Fact]
    public void Fresher_stories_outrank_older_ones_all_else_equal()
    {
        var fresh = ImportanceScoreCalculator.Calculate(Input(ageHours: 1));
        var stale = ImportanceScoreCalculator.Calculate(Input(ageHours: 48));

        Assert.True(fresh > stale);
    }

    [Fact]
    public void Official_sources_count_double_toward_breadth()
    {
        var official = ImportanceScoreCalculator.Calculate(Input(distinctSources: 3, officialSources: 3));
        var unofficial = ImportanceScoreCalculator.Calculate(Input(distinctSources: 3));

        Assert.True(official > unofficial);
    }

    [Fact]
    public void Missing_llm_judgement_falls_back_to_a_neutral_prior()
    {
        Assert.Equal(
            ImportanceScoreCalculator.Calculate(Input(llmImportance: 0.5)),
            ImportanceScoreCalculator.Calculate(Input(llmImportance: null)));
    }

    [Fact]
    public void Category_multiplier_nudges_without_dominating()
    {
        var ai = ImportanceScoreCalculator.Calculate(Input(category: ContentCategory.Ai, llmImportance: 0.5));
        var career = ImportanceScoreCalculator.Calculate(Input(category: ContentCategory.Career, llmImportance: 0.5));

        Assert.True(ai > career);

        // A category boost must never outweigh a real difference in significance.
        var bigCareerStory = ImportanceScoreCalculator.Calculate(
            Input(category: ContentCategory.Career, llmImportance: 1.0));

        Assert.True(bigCareerStory > ai);
    }
}
