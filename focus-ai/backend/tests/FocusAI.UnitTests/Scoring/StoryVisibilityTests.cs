using FocusAI.Application.Common.Mappings;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;
using Xunit;

namespace FocusAI.UnitTests.Scoring;

/// <summary>
/// The bar a finance story has to clear to reach the feed at all.
/// </summary>
/// <remarks>
/// Finance used to have a section of its own, which meant the gate could be
/// generous — anything unfit simply did not appear there. Now finance is a
/// category in the main feed, so the same gate decides whether the story exists
/// for the reader. These pin which side of the line each tier falls on.
/// </remarks>
public class StoryVisibilityTests
{
    private static readonly Func<Story, bool> Visible =
        StoryFilters.VisibleToReaders.Compile();

    private static Story Finance(CommitmentTier tier, bool reversed = false) => new()
    {
        Title = "Faiz kararı",
        Slug = "faiz-karari",
        Category = ContentCategory.Finance,
        Commitment = new StoryCommitment
        {
            Tier = tier,
            Horizon = EventHorizon.Near,
            Instrument = FinanceInstrument.None,
            IsReversed = reversed
        }
    };

    [Theory]
    [InlineData(CommitmentTier.Realized)]
    [InlineData(CommitmentTier.EnactedDated)]
    [InlineData(CommitmentTier.OfficialCommitment)]
    [InlineData(CommitmentTier.ConditionalPending)]
    public void Grounded_finance_reaches_the_feed(CommitmentTier tier) =>
        Assert.True(Visible(Finance(tier)));

    [Theory]
    // A plan is not a decision.
    [InlineData(CommitmentTier.StatedIntent)]
    // The -mış evidential: someone said this happened, and nobody will say who.
    [InlineData(CommitmentTier.UnverifiedClaim)]
    [InlineData(CommitmentTier.AnalystSpeculation)]
    [InlineData(CommitmentTier.Unknown)]
    public void Ungrounded_finance_never_reaches_the_feed(CommitmentTier tier) =>
        Assert.False(Visible(Finance(tier)));

    [Fact]
    public void A_reversed_decision_is_withdrawn_from_the_feed()
    {
        // The strongest tier there is, but the decision was taken back. Leaving it
        // up would be the worst failure this product can have.
        Assert.False(Visible(Finance(CommitmentTier.Realized, reversed: true)));
    }

    [Fact]
    public void Finance_with_no_classification_at_all_is_held_back()
    {
        var story = new Story
        {
            Title = "Piyasalar hareketlendi",
            Slug = "piyasalar",
            Category = ContentCategory.Finance,
            Commitment = null
        };

        // Unclassified is not the same as harmless: the classifier failing must not
        // become a way for hearsay to reach a money-related feed.
        Assert.False(Visible(story));
    }

    [Theory]
    [InlineData(ContentCategory.Ai)]
    [InlineData(ContentCategory.Security)]
    [InlineData(ContentCategory.Software)]
    public void Every_other_category_is_untouched(ContentCategory category)
    {
        // The extra bar is finance-specific. A security story with no commitment
        // row — which is all of them — must not be filtered out by this.
        var story = new Story { Title = "OpenSSH 10.2", Slug = "openssh", Category = category };

        Assert.True(Visible(story));
    }
}
