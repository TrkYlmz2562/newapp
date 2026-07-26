using FocusAI.Domain.Enums;

namespace FocusAI.Application.Common.Interfaces;

/// <summary>
/// What the classifier extracted from a finance story. Every string here is meant
/// to be a verbatim span of the source; the handler verifies that before storing.
/// </summary>
public sealed record CommitmentResult
{
    public bool IsFinance { get; init; }

    public CommitmentTier Tier { get; init; } = CommitmentTier.Unknown;

    public ClaimSource ClaimSource { get; init; } = ClaimSource.Unknown;

    public FinanceInstrument Instrument { get; init; } = FinanceInstrument.None;

    public string? Event { get; init; }

    /// <summary>The date expression exactly as written. Code does the arithmetic.</summary>
    public string? DateText { get; init; }

    /// <summary>The sentence that establishes the tier. Required for publication.</summary>
    public string? Quote { get; init; }

    public string? Condition { get; init; }

    public string? Reference { get; init; }

    /// <summary>
    /// Whether the TEXT determines the tier — not whether the event will happen.
    /// Ambiguous items are excluded rather than guessed at.
    /// </summary>
    public bool Ambiguous { get; init; } = true;

    /// <summary>False when the provider was unavailable, so nothing is classified.</summary>
    public bool Succeeded { get; init; }

    public static readonly CommitmentResult NotClassified = new();
}

/// <summary>
/// Classifies how firmly a finance development is committed. Deliberately separate
/// from <see cref="IContentAiService"/>: it runs only for finance stories, and it is
/// the one call in the product whose output gates what a reader is shown.
/// </summary>
public interface ICommitmentClassifier
{
    /// <summary>Version of the prompt behind this classifier, stored with each result.</summary>
    int Version { get; }

    Task<CommitmentResult> ClassifyAsync(
        StoryPromptContext context,
        CancellationToken cancellationToken = default);
}
