using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Gamification;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Users;

public class User : AuditableEntity
{
    public required string Email { get; set; }

    /// <summary>Lowercased email used for the unique index and for lookups.</summary>
    public required string NormalizedEmail { get; set; }

    public required string DisplayName { get; set; }

    public required string PasswordHash { get; set; }

    public SubscriptionPlan Plan { get; set; } = SubscriptionPlan.Free;

    /// <summary>IANA id, e.g. "Europe/Istanbul". Decides when 08:00 actually is.</summary>
    public string TimeZone { get; set; } = "Europe/Istanbul";

    public string Locale { get; set; } = "tr";

    public bool IsActive { get; set; } = true;

    public DateTimeOffset? LastLoginAt { get; set; }

    public List<string> Roles { get; set; } = ["user"];

    public UserProfile? Profile { get; set; }

    public NotificationSettings? NotificationSettings { get; set; }

    public UserStreak? Streak { get; set; }

    public ICollection<RefreshToken> RefreshTokens { get; set; } = [];

    public ICollection<Bookmark> Bookmarks { get; set; } = [];

    public ICollection<Interaction> Interactions { get; set; } = [];
}
