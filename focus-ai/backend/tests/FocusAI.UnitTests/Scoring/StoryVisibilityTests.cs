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

    /// <summary>Comfortably over the importance floor, so these fixtures test the
    /// finance gate rather than the one below it.</summary>
    private const int Important = StoryFilters.MinImportance + 20;

    private static Story Finance(CommitmentTier tier, bool reversed = false) => new()
    {
        Title = "Faiz kararı",
        Slug = "faiz-karari",
        Category = ContentCategory.Finance,
        ImportanceScore = Important,
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
    public void An_unclassified_finance_story_is_published_plainly_rather_than_held_back()
    {
        var story = new Story
        {
            Title = "Piyasalar hareketlendi",
            Slug = "piyasalar",
            Category = ContentCategory.Finance,
            ImportanceScore = Important,
            Commitment = null
        };

        // The deliberate trade. Holding these back meant the whole category vanished
        // whenever the classifier was unavailable, which is the default install. The
        // story goes out unannotated instead — no badge, no commentary, and the
        // detail page says why it is there.
        Assert.True(Visible(story));
    }

    private static Story Corroborated(int sources, int official = 0, bool evidential = false) => new()
    {
        Title = "Merkez Bankası faiz kararını açıkladı",
        Slug = "faiz",
        Category = ContentCategory.Finance,
        ImportanceScore = Important,
        Commitment = null,
        SourceCount = sources,
        OfficialSourceCount = official,
        HasEvidentialClaim = evidential
    };

    [Theory]
    [InlineData(1, 0)]
    [InlineData(1, 1)]
    [InlineData(4, 2)]
    public void An_unclassified_finance_story_reaches_the_feed_whatever_its_source_count(
        int sources, int official)
    {
        // The path that keeps finance alive with no LLM configured. The classifier
        // is the only step in the pipeline that needs a model; without this the
        // category would be permanently empty rather than merely unannotated.
        //
        // A source-count threshold used to sit here and was removed deliberately: it
        // kept out single-source stories that were perfectly real, and several
        // outlets carrying the same wire copy corroborate the wire, not the fact.
        Assert.True(Visible(Corroborated(sources: sources, official: official)));
    }

    [Fact]
    public void Reported_speech_is_kept_out_however_many_outlets_carry_it()
    {
        // The one bar left on this path, and the reason removing the source count is
        // safe: the -mış evidential marks a claim nobody will stand behind, and
        // repetition does not turn it into a fact.
        Assert.False(Visible(Corroborated(sources: 1, evidential: true)));
        Assert.False(Visible(Corroborated(sources: 5, official: 2, evidential: true)));
    }

    [Fact]
    public void A_classification_overrides_the_unclassified_path()
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
    [InlineData(StoryFilters.MinImportance - 1, false)]
    [InlineData(StoryFilters.MinImportance, true)]
    [InlineData(StoryFilters.MinImportance + 30, true)]
    public void The_importance_floor_applies_to_every_category(int importance, bool expected)
    {
        // Read-time, which is what makes it retroactive: raising the floor hides the
        // back catalogue that no longer clears it without deleting a row, and
        // lowering it brings everything straight back.
        var story = new Story
        {
            Title = "Bir güncelleme daha",
            Slug = "guncelleme",
            Category = ContentCategory.Software,
            ImportanceScore = importance
        };

        Assert.Equal(expected, Visible(story));
    }

    [Fact]
    public void Grounded_finance_still_has_to_clear_the_importance_floor()
    {
        // The two bars are independent, and finance has to pass both: being firmly
        // committed says the development is real, not that it is worth a slot.
        var story = Finance(CommitmentTier.Realized);
        story.ImportanceScore = StoryFilters.MinImportance - 1;

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
        var story = new Story
        {
            Title = "OpenSSH 10.2",
            Slug = "openssh",
            Category = category,
            ImportanceScore = Important
        };

        Assert.True(Visible(story));
    }
}
