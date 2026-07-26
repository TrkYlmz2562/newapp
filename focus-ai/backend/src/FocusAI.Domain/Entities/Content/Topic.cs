using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// A technology, company or concept. Doubles as the vocabulary for user
/// interests (PRD section 5.6), mute lists, trends and learning suggestions.
/// </summary>
public class Topic : AuditableEntity
{
    public required string Name { get; set; }

    public required string Slug { get; set; }

    public TopicKind Kind { get; set; } = TopicKind.Concept;

    public ContentCategory Category { get; set; } = ContentCategory.Unknown;

    /// <summary>
    /// Alternate spellings matched during tagging — ".net", "dotnet", "asp.net core".
    /// Stored lowercase.
    /// </summary>
    public List<string> Aliases { get; set; } = [];

    public string? Description { get; set; }

    /// <summary>Shown in the onboarding interest picker.</summary>
    public bool IsSuggestable { get; set; } = true;

    public ICollection<StoryTopic> Stories { get; set; } = [];
}
