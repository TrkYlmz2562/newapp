using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Learning;

/// <summary>
/// Everything the composer is allowed to put in a brief, gathered by the caller.
/// </summary>
/// <remarks>
/// The composer takes this rather than the entity graph so it stays a pure
/// function of its input: no lazy loads, no "did you Include that", and a test
/// can build the exact shape it wants to assert on.
/// </remarks>
public sealed record BriefCaseFile
{
    /// <summary>Headline of the lesson as a whole.</summary>
    public required string Title { get; init; }

    public IReadOnlyList<BriefStoryFile> Stories { get; init; } = [];

    /// <summary>The reader's declared daily learning budget, in minutes.</summary>
    public int Minutes { get; init; } = 15;

    /// <summary>"Neden bugün bu konu" — only set for the daily suggestion.</summary>
    public string? DailyRationale { get; init; }
}

/// <summary>One story's evidence, as the app holds it.</summary>
public sealed record BriefStoryFile
{
    public required string Title { get; init; }

    public string? Dek { get; init; }

    public string? Summary { get; init; }

    public string? WhyItMatters { get; init; }

    public string? WhoIsAffected { get; init; }

    public IReadOnlyList<string> KeyPoints { get; init; } = [];

    public DateTimeOffset PublishedAt { get; init; }

    public int TrustScore { get; init; }

    public string? TrustExplanation { get; init; }

    /// <summary>The -mış evidential: the text reports the claim rather than asserting it.</summary>
    public bool HasEvidentialClaim { get; init; }

    public IReadOnlyList<string> Topics { get; init; } = [];

    public IReadOnlyList<BriefSourceRef> Sources { get; init; } = [];

    public IReadOnlyList<BriefComparisonPoint> Comparisons { get; init; } = [];

    public BriefCommitmentRef? Commitment { get; init; }

    /// <summary>What the reader wrote on the bookmark, if anything.</summary>
    public string? PersonalNote { get; init; }
}

/// <param name="Url">
/// Where the outlet's earliest piece on this story lives, canonicalised. Null when
/// the article carried no usable address. Carried so the mentor can be pointed at
/// the reporting itself rather than asked to take the file's word for it.
/// </param>
public sealed record BriefSourceRef(
    string Name,
    bool IsOfficial,
    DateTimeOffset PublishedAt,
    string? Url = null);

public sealed record BriefComparisonPoint(
    string Text,
    ComparisonKind Kind,
    string Quote,
    string QuoteSource);

public sealed record BriefCommitmentRef(
    CommitmentTier Tier,
    string? Event,
    string? DateText,
    string? Quote,
    string? Reference,
    string? Condition,
    bool IsReversed);

/// <summary>
/// The part of a brief only a model can write: what this story is worth learning
/// and how to open it. Everything here is optional — a brief without a plan is
/// still a brief, just one where the mentor picks the entry point itself.
/// </summary>
public sealed record BriefPlan
{
    public string? LearningGoal { get; init; }

    /// <summary>Suggested rung, 0-4.</summary>
    public int? EntryLevel { get; init; }

    public string? EntryReason { get; init; }

    /// <summary>What a 40-line demo of this would be. Null when the topic is not code.</summary>
    public string? DemoIdea { get; init; }

    /// <summary>Which diagram form fits, when a demo does not.</summary>
    public string? DiagramIdea { get; init; }

    public IReadOnlyList<string> AnchorQuestions { get; init; } = [];

    public string? CommonMistake { get; init; }

    /// <summary>Something worth knowing that the case file does not answer.</summary>
    public string? OpenQuestion { get; init; }

    public string? Provider { get; init; }

    public string? Model { get; init; }
}
