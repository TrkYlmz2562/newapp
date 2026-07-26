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
    /// <summary>Fewest independent outlets that count as corroboration on their own.</summary>
    private const int CorroborationThreshold = 2;

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
    /// <b>Corroborated.</b> No classification, but the newsroom test: an official
    /// source filed it, or two independent outlets report the same thing. This is
    /// what keeps finance alive when no LLM is configured — the classifier is the
    /// only step here that needs one, so without this path the category would be
    /// permanently empty rather than merely unannotated. Nothing is inferred on this
    /// path and nothing is commented on; the story is published plainly and the card
    /// carries no commitment badge, because there is no classification to show.
    ///
    /// The evidential check guards the corroborated path specifically: three outlets
    /// repeating the same rumour corroborate the rumour, not the fact. It is
    /// deterministic text analysis, so it still works with no model — which is
    /// exactly the situation this path exists for. The classified path does not need
    /// it, because a model that read the text already graded that claim as
    /// UnverifiedClaim.
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
        (story.Commitment == null &&
         !story.HasEvidentialClaim &&
         (story.OfficialSourceCount >= 1 || story.SourceCount >= CorroborationThreshold));
}
