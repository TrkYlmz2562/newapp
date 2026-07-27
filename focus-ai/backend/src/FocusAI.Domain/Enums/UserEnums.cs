namespace FocusAI.Domain.Enums;

public enum ExperienceLevel
{
    Student = 0,
    Junior = 1,
    Mid = 2,
    Senior = 3,
    Staff = 4,
    Lead = 5
}

public enum SubscriptionPlan
{
    Free = 0,
    Premium = 1,
    Team = 2
}

/// <summary>
/// Signals the personalization model learns from. Ordering matters only for
/// readability; weights live in <see cref="Scoring.PersonalizationScorer"/>.
/// </summary>
public enum InteractionType
{
    Impression = 0,
    Open = 1,
    ReadComplete = 2,
    Save = 3,
    Unsave = 4,
    Helpful = 5,
    NotHelpful = 6,
    Dismiss = 7,
    Share = 8,
    SourceClick = 9
}

/// <summary>Delivery windows from PRD section 10.</summary>
public enum DigestSlot
{
    Morning = 0,
    Evening = 1,
    BigNewsOnly = 2,
    Weekly = 3
}

public enum DigestPeriod
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2
}

public enum LearningStatus
{
    Suggested = 0,
    Started = 1,
    Completed = 2,
    Skipped = 3
}

/// <summary>
/// Lifecycle of a learning brief. The split between the first two states is the
/// cost boundary: queueing is free, generating spends a model call.
/// </summary>
public enum BriefStatus
{
    /// <summary>Reader asked to learn this. Nothing generated yet.</summary>
    Queued = 0,

    /// <summary>Prompt produced and stored, ready to copy.</summary>
    Generated = 1,

    /// <summary>Reader finished the lesson.</summary>
    Done = 2
}

public enum BriefOrigin
{
    Story = 0,
    DailySuggestion = 1
}
