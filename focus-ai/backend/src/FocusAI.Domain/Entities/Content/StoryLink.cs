using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// Supplementary material on the detail page: the GitHub repo, the paper, the
/// conference talk (PRD section 8).
/// </summary>
public class StoryLink : BaseEntity
{
    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public StoryLinkKind Kind { get; set; } = StoryLinkKind.Article;

    public required string Url { get; set; }

    public required string Title { get; set; }

    public string? Description { get; set; }

    public string? ThumbnailUrl { get; set; }

    public int Position { get; set; }
}
