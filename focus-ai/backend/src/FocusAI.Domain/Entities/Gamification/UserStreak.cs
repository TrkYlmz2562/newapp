using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Users;

namespace FocusAI.Domain.Entities.Gamification;

/// <summary>
/// PRD section 15. The note in the PRD is load-bearing: this exists to reward
/// consistency, not to manufacture compulsion. There is no streak-loss push, no
/// escalating counter, and the streak caps its own visual prominence.
/// </summary>
public class UserStreak : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    public int CurrentStreak { get; set; }

    public int LongestStreak { get; set; }

    /// <summary>Last local day on which the user read at least one story.</summary>
    public DateOnly? LastActiveDate { get; set; }

    /// <summary>Days per week the user is aiming for. Default is deliberately not 7.</summary>
    public int WeeklyGoalDays { get; set; } = 5;

    public int CompletedLearnings { get; set; }

    public int TotalStoriesRead { get; set; }

    public ICollection<UserBadge> Badges { get; set; } = [];

    /// <summary>
    /// Records activity for a local calendar day. Same-day calls are idempotent;
    /// a one-day gap continues the streak, a longer gap restarts it at 1.
    /// </summary>
    public void RegisterActivity(DateOnly localDate)
    {
        if (LastActiveDate == localDate)
        {
            return;
        }

        CurrentStreak = LastActiveDate is { } last && last.AddDays(1) == localDate
            ? CurrentStreak + 1
            : 1;

        LastActiveDate = localDate;
        LongestStreak = Math.Max(LongestStreak, CurrentStreak);
    }
}

public class Badge : BaseEntity
{
    public required string Slug { get; set; }

    public required string Name { get; set; }

    public required string Description { get; set; }

    public string? Emoji { get; set; }
}

public class UserBadge : BaseEntity
{
    public Guid UserStreakId { get; set; }

    public UserStreak? UserStreak { get; set; }

    public Guid BadgeId { get; set; }

    public Badge? Badge { get; set; }

    public DateTimeOffset AwardedAt { get; set; }
}
