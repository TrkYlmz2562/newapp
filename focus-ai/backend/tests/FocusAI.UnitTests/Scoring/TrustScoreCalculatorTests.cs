using FocusAI.Domain.Scoring;
using Xunit;

namespace FocusAI.UnitTests.Scoring;

public class TrustScoreCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static TrustScoreInput Input(
        int distinctSources = 1,
        int officialSources = 0,
        double maxWeight = 0.6,
        int ageHours = 2,
        int engagement = 0,
        double? technicalAccuracy = null) =>
        new()
        {
            DistinctSourceCount = distinctSources,
            OfficialSourceCount = officialSources,
            MaxSourceTrustWeight = maxWeight,
            PublishedAt = Now.AddHours(-ageHours),
            Now = Now,
            EngagementScore = engagement,
            TechnicalAccuracy = technicalAccuracy
        };

    [Fact]
    public void Total_is_always_within_zero_to_hundred()
    {
        var floor = TrustScoreCalculator.Calculate(Input(
            distinctSources: 0, officialSources: 0, maxWeight: 0, ageHours: 10_000, technicalAccuracy: 0));

        var ceiling = TrustScoreCalculator.Calculate(Input(
            distinctSources: 50, officialSources: 10, maxWeight: 1, ageHours: 0,
            engagement: 100_000, technicalAccuracy: 1));

        Assert.InRange(floor.Total, 0, 100);
        Assert.InRange(ceiling.Total, 0, 100);
        Assert.True(ceiling.Total > floor.Total);
    }

    [Fact]
    public void Official_source_outranks_a_pile_of_unofficial_ones()
    {
        var official = TrustScoreCalculator.Calculate(Input(distinctSources: 1, officialSources: 1));
        var unofficial = TrustScoreCalculator.Calculate(Input(distinctSources: 3, maxWeight: 0.5));

        // The whole point of the "resmi kaynak" criterion: a vendor announcing
        // its own release beats three blogs repeating a rumour.
        Assert.True(official.OfficialSourceScore > unofficial.OfficialSourceScore);
    }

    [Fact]
    public void Corroboration_rises_with_sources_and_saturates()
    {
        var one = TrustScoreCalculator.Calculate(Input(distinctSources: 1)).CorroborationScore;
        var three = TrustScoreCalculator.Calculate(Input(distinctSources: 3)).CorroborationScore;
        var eight = TrustScoreCalculator.Calculate(Input(distinctSources: 8)).CorroborationScore;
        var fifty = TrustScoreCalculator.Calculate(Input(distinctSources: 50)).CorroborationScore;

        Assert.True(one < three);
        Assert.True(three < eight);
        Assert.Equal(100, eight);

        // Saturation matters: without it, an aggregator storm would out-score a
        // first-party announcement purely on volume.
        Assert.Equal(eight, fifty);
    }

    [Fact]
    public void Recency_decays_but_never_below_the_floor()
    {
        var fresh = TrustScoreCalculator.Calculate(Input(ageHours: 1)).RecencyScore;
        var oneDay = TrustScoreCalculator.Calculate(Input(ageHours: 24)).RecencyScore;
        var ancient = TrustScoreCalculator.Calculate(Input(ageHours: 24 * 365)).RecencyScore;

        Assert.Equal(100, fresh);
        Assert.True(oneDay < fresh);
        Assert.True(ancient >= 20);
    }

    [Fact]
    public void Feed_published_in_the_future_is_treated_as_fresh_not_as_an_error()
    {
        // Misconfigured server clocks are common enough that a negative age must
        // not produce a nonsense score.
        var result = TrustScoreCalculator.Calculate(Input(ageHours: -5));

        Assert.Equal(100, result.RecencyScore);
        Assert.InRange(result.Total, 0, 100);
    }

    [Fact]
    public void Missing_technical_accuracy_uses_a_neutral_prior()
    {
        var unjudged = TrustScoreCalculator.Calculate(Input(technicalAccuracy: null));
        var neutral = TrustScoreCalculator.Calculate(Input(technicalAccuracy: 0.6));

        Assert.Equal(neutral.TechnicalAccuracyScore, unjudged.TechnicalAccuracyScore);
    }

    [Fact]
    public void Explanation_reports_source_counts_in_turkish()
    {
        var result = TrustScoreCalculator.Calculate(Input(distinctSources: 4, officialSources: 2));

        Assert.Contains("2 resmi kaynak", result.Explanation);
        Assert.Contains("4 kaynak doğruladı", result.Explanation);
    }

    [Fact]
    public void Single_unverified_source_reads_as_low_trust()
    {
        var result = TrustScoreCalculator.Calculate(Input(
            distinctSources: 1, maxWeight: 0.4, ageHours: 200, technicalAccuracy: 0.2));

        Assert.True(result.Total < 50);
    }
}
