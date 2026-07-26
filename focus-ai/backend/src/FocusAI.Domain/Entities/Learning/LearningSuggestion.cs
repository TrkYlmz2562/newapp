using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Entities.Users;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Learning;

/// <summary>
/// "Öğrenme Modu" from PRD section 5.7 — one bounded topic per day, sized to
/// the user's declared daily learning budget.
/// </summary>
public class LearningSuggestion : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public DateOnly Date { get; set; }

    public Guid? TopicId { get; set; }

    public Topic? Topic { get; set; }

    /// <summary>e.g. "MCP (Model Context Protocol) temelleri".</summary>
    public required string Title { get; set; }

    /// <summary>"Neden?" — the justification that makes the suggestion feel earned.</summary>
    public required string Rationale { get; set; }

    public int EstimatedMinutes { get; set; } = 15;

    public LearningStatus Status { get; set; } = LearningStatus.Suggested;

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Story that triggered the suggestion, so the user can trace it back.</summary>
    public Guid? SourceStoryId { get; set; }

    public ICollection<LearningResource> Resources { get; set; } = [];
}

public class LearningResource : BaseEntity
{
    public Guid LearningSuggestionId { get; set; }

    public LearningSuggestion? LearningSuggestion { get; set; }

    public required string Title { get; set; }

    public required string Url { get; set; }

    /// <summary>"docs" | "video" | "repo" | "article" | "paper".</summary>
    public string Kind { get; set; } = "article";

    public int EstimatedMinutes { get; set; }

    public int Position { get; set; }
}
