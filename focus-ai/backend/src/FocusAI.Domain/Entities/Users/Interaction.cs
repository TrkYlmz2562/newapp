using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Users;

/// <summary>
/// Behavioural signal used both for personalisation and for the KPIs in PRD
/// section 18 (read-through rate, notification engagement, dwell time).
/// </summary>
public class Interaction : BaseEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public InteractionType Type { get; set; }

    /// <summary>Seconds the story was on screen; only meaningful for Open/ReadComplete.</summary>
    public int? DwellSeconds { get; set; }

    /// <summary>Where the interaction happened — "digest", "home", "search", "push".</summary>
    public string? Surface { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
