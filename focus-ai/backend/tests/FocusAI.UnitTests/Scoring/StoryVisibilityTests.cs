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

    private static Story Corroborated(int sources, int official = 0, bool evidential = false) => new()
    {
        Title = "Merkez Bankası faiz kararını açıkladı",
        Slug = "faiz",
        Category = ContentCategory.Finance,
        Commitment = null,
        SourceCount = sources,
        OfficialSourceCount = official,
        HasEvidentialClaim = evidential
    };

    [Fact]
    public void Two_independent_outlets_are_enough_without_any_classification()
    {
        // The path that keeps finance alive with no LLM configured. The classifier
        // is the only step in the pipeline that needs a model; without this the
        // category would be permanently empty rather than merely unannotated.
        Assert.True(Visible(Corroborated(sources: 2)));
    }

    [Fact]
    public void One_outlet_alone_is_not_corroboration()
    {
        Assert.False(Visible(Corroborated(sources: 1)));
    }

    [Fact]
    public void An_official_source_needs_no_second_outlet()
    {
        // A central bank publishing its own rate decision is the record itself, not
        // a claim about someone else, so there is nothing to corroborate it against.
        Assert.True(Visible(Corroborated(sources: 1, official: 1)));
    }

    [Fact]
    public void Corroborated_hearsay_is_still_hearsay()
    {
        // The reason the evidential check exists: three outlets repeating the same
        // rumour corroborate the rumour, not the fact. Deterministic, so it still
        // works with no model — which is exactly the situation this path is for.
        Assert.False(Visible(Corroborated(sources: 3, evidential: true)));
    }

    [Fact]
    public void A_classification_overrides_the_corroboration_path()
    {
        // A single-source story the model graded as an official commitment gets in;
        // a well-corroborated one it graded as an unverified claim does not. The
        // model read the text, so its verdict is the better evidence.
        var classified = Finance(CommitmentTier.OfficialCommitment);
        classified.SourceCount = 1;

        var rejected = Finance(CommitmentTier.UnverifiedClaim);
        rejected.SourceCount = 5;
        rejected.OfficialSourceCount = 2;

        Assert.True(Visible(classified));
        Assert.False(Visible(rejected));
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
