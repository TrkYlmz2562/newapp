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
    /// a section of its own because a finance development is a tech story like any
    /// other — it competes for the reader's attention on importance, alongside
    /// everything else. What it must not do is arrive on hearsay. The classifier
    /// already grades every finance story by how firmly it is committed; this keeps
    /// the four grounded tiers and drops the two that are not:
    ///
    ///   kept    Realized, EnactedDated, OfficialCommitment, ConditionalPending
    ///   dropped StatedIntent (a plan), UnverifiedClaim (the -mış evidential), Unknown
    ///
    /// Deliberately coarser than <see cref="Domain.Text.CommitmentLexicon.IsPublishable"/>,
    /// which answers a different question — "is this firm enough to headline a
    /// forward-looking list" — and still decides how the card is badged. Here the
    /// question is only "is this grounded, or is it gossip", because the ranker
    /// already decides whether it is important enough to show.
    ///
    /// Written as an OR over named values rather than a range over the enum's
    /// numbers: it survives someone reordering the enum, and it reads as the rule.
    /// </remarks>
    public static Expression<Func<Story, bool>> VisibleToReaders => story =>
        story.Category != ContentCategory.Finance ||
        (story.Commitment != null &&
         !story.Commitment.IsReversed &&
         (story.Commitment.Tier == CommitmentTier.Realized ||
          story.Commitment.Tier == CommitmentTier.EnactedDated ||
          story.Commitment.Tier == CommitmentTier.OfficialCommitment ||
          story.Commitment.Tier == CommitmentTier.ConditionalPending));
}
