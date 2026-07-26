using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// How far the actor behind a finance story has bound itself, and by when. This is
/// what the Finans feed gates on.
/// </summary>
/// <remarks>
/// Every field here is either extracted verbatim from the source or computed in
/// code. Nothing is a prediction: the product never claims an event will happen,
/// only that it has been formally committed to — a property of the document, which
/// the reader can check against the quote and the reference shown on the card.
/// </remarks>
public class StoryCommitment : AuditableEntity
{
    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    /// <summary>Final tier after the code-side lexicon has had its say.</summary>
    public CommitmentTier Tier { get; set; } = CommitmentTier.Unknown;

    /// <summary>What the model alone said, kept so the two can be compared.</summary>
    public CommitmentTier ModelTier { get; set; } = CommitmentTier.Unknown;

    public EventHorizon Horizon { get; set; } = EventHorizon.Unknown;

    public ClaimSource ClaimSource { get; set; } = ClaimSource.Unknown;

    public FinanceInstrument Instrument { get; set; } = FinanceInstrument.None;

    /// <summary>The core event in one clause, as stated in the source.</summary>
    public string? Event { get; set; }

    /// <summary>The date expression exactly as it appears in the text.</summary>
    public string? DateText { get; set; }

    /// <summary>Parsed in code from <see cref="DateText"/>; null when unresolvable.</summary>
    public DateOnly? EventDate { get; set; }

    public DatePrecision DatePrecision { get; set; } = DatePrecision.None;

    /// <summary>
    /// The one sentence from the source that establishes the tier. Nothing reaches
    /// the feed without it — if a claim cannot be quoted, it cannot be made.
    /// </summary>
    public string? Quote { get; set; }

    /// <summary>What still has to clear, for conditional items.</summary>
    public string? Condition { get; set; }

    /// <summary>Institution + document + date + number, shown as "Dayanak".</summary>
    public string? Reference { get; set; }

    /// <summary>True once the story says the event was cancelled, withdrawn or postponed.</summary>
    public bool IsReversed { get; set; }

    /// <summary>
    /// Bumped whenever the prompt or the lexicon changes, so stored tiers are never
    /// carried forward across a classifier that no longer produced them.
    /// </summary>
    public int ClassifierVersion { get; set; }

    public DateTimeOffset ClassifiedAt { get; set; }
}
