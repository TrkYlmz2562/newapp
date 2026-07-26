using System.Linq.Expressions;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;

namespace FocusAI.Application.Common.Mappings;

/// <summary>
/// Predicates shared by every query that puts stories in front of a reader, so the
/// feed, the digest and search cannot drift into disagreeing about what is
/// publishable.
/// </summary>
public static class StoryFilters
{
    /// <summary>
    /// Whether a story may appear in a reader-facing list.
    /// </summary>
    /// <remarks>
    /// Finance is the only category with an extra bar, and it is here rather than in
    /// a section of its own because a finance development is a story like any other
    /// — it competes for attention on importance, alongside everything else. What it
    /// must not do is arrive on hearsay.
    ///
    /// There are two independent ways past the bar, and a finance story needs only
    /// one of them.
    ///
    /// <b>Classified.</b> The model graded how firmly the development is committed.
    /// Four tiers are grounded — Realized, EnactedDated, OfficialCommitment,
    /// ConditionalPending — and the rest are not: StatedIntent is a plan,
    /// UnverifiedClaim is the -mış evidential, AnalystSpeculation is an opinion.
    /// A reversed decision drops out whatever its tier, because leaving a withdrawn
    /// decision up is the worst failure this product can have.
    ///
    /// <b>Unclassified but not hearsay.</b> No classification, and the only bar is
    /// that the story does not report its claim at second hand. This is what keeps
    /// finance alive when no LLM is configured — the classifier is the only step
    /// here that needs one, so without this path the category would be permanently
    /// empty rather than merely unannotated. Nothing is inferred on this path and
    /// nothing is commented on; the story is published plainly and the card carries
    /// no commitment badge, because there is no classification to show.
    ///
    /// A source-count threshold used to sit here too and was deliberately removed:
    /// requiring two outlets kept out single-source stories that were perfectly
    /// real, and corroboration is a weaker guarantee than it looks anyway — several
    /// outlets carrying the same wire copy corroborate the wire, not the fact. That
    /// leaves the evidential check doing the work on its own, which is the honest
    /// position: it is deterministic text analysis of what the story itself claims,
    /// it still works with no model, and it is aimed squarely at the -mış reported
    /// speech that marks a claim nobody will stand behind. The classified path does
    /// not need it, because a model that read the text already graded such a claim
    /// as UnverifiedClaim.
    ///
    /// Coarser than <see cref="Domain.Text.CommitmentLexicon.IsPublishable"/>, which
    /// answers a different question — "is this firm enough to headline a
    /// forward-looking list" — and still decides how the card is badged. Here the
    /// question is only "is this grounded, or is it gossip", because the ranker
    /// already decides whether it is important enough to show.
    ///
    /// Tiers are written as an OR over named values rather than a range over the
    /// enum's numbers: it survives someone reordering the enum, and it reads as the
    /// rule.
    /// </remarks>
    public static Expression<Func<Story, bool>> VisibleToReaders => story =>
        story.Category != ContentCategory.Finance ||
        (story.Commitment != null &&
         !story.Commitment.IsReversed &&
         (story.Commitment.Tier == CommitmentTier.Realized ||
          story.Commitment.Tier == CommitmentTier.EnactedDated ||
          story.Commitment.Tier == CommitmentTier.OfficialCommitment ||
          story.Commitment.Tier == CommitmentTier.ConditionalPending)) ||
        (story.Commitment == null && !story.HasEvidentialClaim);
}
