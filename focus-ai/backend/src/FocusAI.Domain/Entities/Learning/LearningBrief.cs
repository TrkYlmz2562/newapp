using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Entities.Users;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Learning;

/// <summary>
/// A story the reader wants to learn from, and the prompt that turns it into a
/// lesson with <see cref="Domain.Learning.FocusMentorPersona"/>.
/// </summary>
/// <remarks>
/// Two lifecycles in one row, which is why <see cref="Status"/> exists rather than
/// a separate queue table. Pressing "Bunu öğren" writes a
/// <see cref="BriefStatus.Queued"/> row and costs nothing — no model is called.
/// The model runs only when the reader asks for the prompt, and what it produces
/// is stored, so the same brief is never paid for twice.
///
/// <see cref="Prompt"/> holds the rendered text rather than being re-composed on
/// read. Coverage keeps arriving after a story is published, so re-rendering would
/// mean the text a reader copied yesterday is not the text they see today — and a
/// lesson already in progress would silently disagree with its own source.
/// </remarks>
public class LearningBrief : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Headline of the lesson: the story's title, or the day's topic.</summary>
    public required string Title { get; set; }

    public BriefStatus Status { get; set; } = BriefStatus.Queued;

    public BriefOrigin Origin { get; set; } = BriefOrigin.Story;

    /// <summary>Set only when this brief was queued from the daily suggestion.</summary>
    public Guid? LearningSuggestionId { get; set; }

    public LearningSuggestion? LearningSuggestion { get; set; }

    /// <summary>The text the reader copies. Null until generated.</summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// One sentence naming what this lesson is for, from the planner. Shown on the
    /// card so the reader can tell what they are about to open.
    /// </summary>
    public string? LearningGoal { get; set; }

    /// <summary>Suggested rung of the ladder, 0-4. Null when the planner did not run.</summary>
    public int? EntryLevel { get; set; }

    /// <summary>What the mentor could build to show this, in one line.</summary>
    public string? DemoIdea { get; set; }

    /// <summary>
    /// True when the prompt was assembled without the planner — the model was off
    /// or failed. The case file is still there, so the brief is usable; the reader
    /// is told which one they got rather than being handed a quietly thinner file.
    /// </summary>
    public bool PlannerUnavailable { get; set; }

    public int PersonaVersion { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public DateTimeOffset? GeneratedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Modelled as many from the start. A reader with four saved stories about one
    /// topic is better served by one lesson than by four, and discovering that after
    /// the table ships means a migration rather than a query change.
    /// </summary>
    public ICollection<LearningBriefStory> Stories { get; set; } = [];
}

public class LearningBriefStory : BaseEntity
{
    public Guid LearningBriefId { get; set; }

    public LearningBrief? LearningBrief { get; set; }

    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public int Position { get; set; }
}
