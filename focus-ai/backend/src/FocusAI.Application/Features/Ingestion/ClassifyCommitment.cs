using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Text;

namespace FocusAI.Application.Features.Ingestion;

/// <summary>
/// Turns a classifier answer into a stored commitment, refusing anything it cannot
/// check.
/// </summary>
/// <remarks>
/// Two rules do the work here. First, every span the model returns must be found
/// in the article text — a quote that is not in the source is a fabrication, and
/// the item is dropped rather than merely losing that field. Second, the code-side
/// lexicon runs independently and can only make the tier *less* certain, never
/// more: a one-way ratchet toward caution, so a model that misreads "imzalanmış"
/// as first-hand cannot promote a rumour into the feed.
/// </remarks>
public static class CommitmentEvaluator
{
    public static StoryCommitment? Evaluate(
        CommitmentResult result,
        string corpus,
        DateTimeOffset publishedAt,
        DateTimeOffset now,
        int classifierVersion)
    {
        if (!result.Succeeded || !result.IsFinance || result.Tier == CommitmentTier.Unknown)
        {
            return null;
        }

        // Ambiguity is the model saying the text does not settle the question. The
        // honest response is to keep the item out, not to pick the friendlier reading.
        if (result.Ambiguous)
        {
            return null;
        }

        // A claim that cannot be quoted cannot be made: the card shows this sentence
        // so the reader can check the classification themselves.
        if (string.IsNullOrWhiteSpace(result.Quote) || !OccursIn(result.Quote, corpus))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(result.DateText) && !OccursIn(result.DateText, corpus))
        {
            return null;
        }

        var (eventDate, precision) = TurkishDate.Parse(result.DateText, publishedAt);

        // The lexicon reads the same text the model did and reports the most it
        // permits; the worse of the two wins.
        var lexiconTier = CommitmentLexicon.Detect(corpus);
        var tier = CommitmentLexicon.Worse(result.Tier, lexiconTier);

        // No resolvable date means no "when", and without a when nothing can be
        // called near-certain — except an event that has already happened.
        if (eventDate is null && tier != CommitmentTier.Realized)
        {
            tier = CommitmentLexicon.Worse(tier, CommitmentTier.StatedIntent);
        }

        var horizon = tier == CommitmentTier.Realized && eventDate is null
            ? EventHorizon.Completed
            : CommitmentLexicon.Horizon(eventDate, now);

        return new StoryCommitment
        {
            Tier = tier,
            ModelTier = result.Tier,
            Horizon = horizon,
            ClaimSource = result.ClaimSource,
            Instrument = result.Instrument,
            Event = FieldLimits.Cap(result.Event, FieldLimits.CommitmentEvent),
            DateText = FieldLimits.Cap(result.DateText, FieldLimits.CommitmentDateText),
            EventDate = eventDate,
            DatePrecision = precision,
            Quote = FieldLimits.Cap(result.Quote, FieldLimits.CommitmentQuote),
            Condition = FieldLimits.Cap(result.Condition, FieldLimits.CommitmentQuote),
            Reference = FieldLimits.Cap(result.Reference, FieldLimits.CommitmentReference),
            IsReversed = CommitmentLexicon.IsReversal(corpus),
            ClassifierVersion = classifierVersion,
            ClassifiedAt = now,
            CreatedAt = now
        };
    }

    /// <summary>
    /// Folded containment: the model may normalise whitespace or casing when it
    /// copies, and rejecting a genuine quote over a double space would push honest
    /// items out of the feed for no gain.
    /// </summary>
    private static bool OccursIn(string span, string corpus)
    {
        var needle = CommitmentLexicon.Fold(span);
        return needle.Length > 0 && CommitmentLexicon.Fold(corpus).Contains(needle, StringComparison.Ordinal);
    }
}
