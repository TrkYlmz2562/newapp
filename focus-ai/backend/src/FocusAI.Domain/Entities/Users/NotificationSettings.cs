using FocusAI.Domain.Common;

namespace FocusAI.Domain.Entities.Users;

/// <summary>PRD section 10 — deliberately four switches, not a settings maze.</summary>
public class NotificationSettings : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public bool MorningDigest { get; set; } = true;

    public bool EveningDigest { get; set; }

    /// <summary>Breaking-news pushes, gated on <see cref="BigNewsThreshold"/>.</summary>
    public bool BigNewsOnly { get; set; } = true;

    public bool WeeklyDigest { get; set; } = true;

    /// <summary>Minimum importance score that justifies interrupting someone.</summary>
    public int BigNewsThreshold { get; set; } = 85;

    /// <summary>Local hour after which no push is sent. Attention hygiene, per PRD section 15.</summary>
    public int QuietHoursStart { get; set; } = 22;

    public int QuietHoursEnd { get; set; } = 7;

    public ICollection<PushSubscription> PushSubscriptions { get; set; } = [];
}

/// <summary>W3C Web Push subscription registered by the PWA service worker.</summary>
public class PushSubscription : BaseEntity
{
    public Guid NotificationSettingsId { get; set; }

    public NotificationSettings? NotificationSettings { get; set; }

    public required string Endpoint { get; set; }

    public required string P256dh { get; set; }

    public required string Auth { get; set; }

    public string? UserAgent { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastUsedAt { get; set; }
}
