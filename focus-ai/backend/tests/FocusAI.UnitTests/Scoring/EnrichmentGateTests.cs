using FocusAI.Domain.Enums;
using FocusAI.Domain.Scoring;
using Xunit;

namespace FocusAI.UnitTests.Scoring;

/// <summary>
/// Where the gate cuts, written down as policy rather than arithmetic.
/// </summary>
/// <remarks>
/// This is the one decision in the pipeline nobody can see being made. A story the
/// gate turns away is never read by a model and never appears anywhere; the feed
/// simply looks quieter, and there is no screen that says why. So the cases are
/// pinned here, where moving the threshold or reweighting a calculator has to come
/// and argue with them first.
/// </remarks>
public class EnrichmentGateTests
{
    /// <summary>Real age is irrelevant to the gate; this only has to be a date.</summary>
    private static readonly DateTimeOffset Published = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    private static EnrichmentGateInput Story(
        int sources = 1,
        int official = 0,
        double trustWeight = 0.8,
        int engagement = 0,
        ContentCategory category = ContentCategory.Software) => new()
    {
        DistinctSourceCount = sources,
        OfficialSourceCount = official,
        MaxSourceTrustWeight = trustWeight,
        EngagementScore = engagement,
        PublishedAt = Published,
        Category = category
    };

    [Fact]
    public void A_lone_report_nobody_picked_up_and_nobody_is_reading_is_turned_away()
    {
        // The bulk of what the pipeline was paying to summarise. No second outlet,
        // no first-party source, no community interest: there is no evidence here
        // that a model would find anything, so no call is made.
        Assert.False(EnrichmentGate.IsWorthEnriching(Story()));
        Assert.False(EnrichmentGate.IsWorthEnriching(Story(trustWeight: 0.4)));
    }

    [Fact]
    public void A_second_outlet_is_enough()
    {
        // Corroboration is the cheapest real signal available without a model:
        // another newsroom independently thought this was worth writing.
        Assert.True(EnrichmentGate.IsWorthEnriching(Story(sources: 2)));
        Assert.True(EnrichmentGate.IsWorthEnriching(Story(sources: 3)));
    }

    [Fact]
    public void A_first_party_source_carries_a_single_report()
    {
        // A vendor announcing its own release is not "one blog said so", and the
        // breadth term counts official sources twice for exactly this reason.
        Assert.True(EnrichmentGate.IsWorthEnriching(Story(sources: 1, official: 1)));
    }

    [Fact]
    public void Community_interest_carries_a_single_report()
    {
        // The escape hatch for a real scoop: if people are already reading and
        // discussing it, that is evidence the gate can see without a model.
        Assert.True(EnrichmentGate.IsWorthEnriching(Story(sources: 1, engagement: 500)));
    }

    [Fact]
    public void Age_does_not_decide_it()
    {
        // The reason the reference age exists. At the moment of enrichment every
        // story has just arrived, so freshness would hand the same bonus to all of
        // them and the gate would be a clock. Two stories with identical evidence
        // must get identical answers whether they are enriched at once or after a
        // backlog — so the same input published a week apart scores the same.
        var today = Story(sources: 2);
        var lastWeek = today with { PublishedAt = Published.AddDays(-7) };

        Assert.Equal(
            EnrichmentGate.ProjectedImportance(today),
            EnrichmentGate.ProjectedImportance(lastWeek));
    }

    [Fact]
    public void Evidence_moves_the_projection_in_the_direction_you_would_expect()
    {
        var lone = EnrichmentGate.ProjectedImportance(Story());
        var corroborated = EnrichmentGate.ProjectedImportance(Story(sources: 3));
        var wellSourced = EnrichmentGate.ProjectedImportance(Story(sources: 5, official: 2));

        Assert.True(lone < corroborated);
        Assert.True(corroborated < wellSourced);
    }

    [Fact]
    public void The_category_thumb_never_decides_it_on_its_own()
    {
        // The multiplier is a nudge by design. It must not be able to rescue a
        // story with no evidence behind it, or the gate becomes a category filter.
        foreach (var category in Enum.GetValues<ContentCategory>())
        {
            Assert.False(
                EnrichmentGate.IsWorthEnriching(Story(category: category)),
                $"A lone unread report in {category} should not clear the gate.");
        }
    }

    [Fact]
    public void A_story_with_no_sources_recorded_is_scored_rather_than_thrown()
    {
        Assert.InRange(EnrichmentGate.ProjectedImportance(Story(sources: 0)), 0, 100);
    }
}
