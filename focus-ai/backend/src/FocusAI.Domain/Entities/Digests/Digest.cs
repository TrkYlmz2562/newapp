using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Entities.Users;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Digests;

/// <summary>
/// A materialised reading session: "Günün Bilmen Gereken 10 Konusu"
/// (PRD sections 5.1, 5.8, 5.9).
/// </summary>
/// <remarks>
/// <see cref="UserId"/> null means the shared editorial edition that anonymous
/// visitors and new users see before personalisation has anything to work with.
/// </remarks>
public class Digest : AuditableEntity
{
    public Guid? UserId { get; set; }

    public User? User { get; set; }

    public DigestPeriod Period { get; set; } = DigestPeriod.Daily;

    /// <summary>The local calendar day (or ISO week start) this digest covers.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Two-sentence AI-written intro that frames the day.</summary>
    public string? Intro { get; set; }

    public string Language { get; set; } = "tr";

    /// <summary>Sum of member reading times — the PRD promises about 5 minutes.</summary>
    public int ReadingMinutes { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }

    public ICollection<DigestItem> Items { get; set; } = [];
}

public class DigestItem : BaseEntity
{
    public Guid DigestId { get; set; }

    public Digest? Digest { get; set; }

    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public int Rank { get; set; }

    /// <summary>Why this story made the cut for this reader — shown as a one-liner.</summary>
    public string? Reason { get; set; }

    /// <summary>Blended score at generation time; kept for offline ranking analysis.</summary>
    public double Score { get; set; }
}
