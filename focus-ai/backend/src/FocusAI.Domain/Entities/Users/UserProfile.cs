using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Users;

/// <summary>The "Bana Özel" profile from PRD sections 5.6 and 14.</summary>
public class UserProfile : AuditableEntity
{
    public Guid UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Free-text role, e.g. "Senior .NET & Angular developer".</summary>
    public string? Headline { get; set; }

    public ExperienceLevel ExperienceLevel { get; set; } = ExperienceLevel.Mid;

    /// <summary>Preferred hour (local) for the morning digest. PRD default is 08:00.</summary>
    public int DailyDigestHour { get; set; } = 8;

    /// <summary>Cap on how many stories the daily digest carries. PRD default is 10.</summary>
    public int DailyStoryCount { get; set; } = 10;

    /// <summary>Minutes the user wants to spend learning each day (PRD section 5.7).</summary>
    public int DailyLearningMinutes { get; set; } = 15;

    public bool OnboardingCompleted { get; set; }

    public ICollection<UserInterest> Interests { get; set; } = [];

    public ICollection<UserMutedTopic> MutedTopics { get; set; } = [];

    public ICollection<UserFavoriteSource> FavoriteSources { get; set; } = [];
}

/// <summary>Weighted interest — the weight is nudged by reading behaviour over time.</summary>
public class UserInterest : BaseEntity
{
    public Guid UserProfileId { get; set; }

    public UserProfile? UserProfile { get; set; }

    public Guid TopicId { get; set; }

    public Topic? Topic { get; set; }

    /// <summary>[0,1]. Explicit onboarding picks start at 1.0; inferred ones start lower.</summary>
    public double Weight { get; set; } = 1.0;

    /// <summary>False when the weight was inferred from behaviour rather than chosen.</summary>
    public bool IsExplicit { get; set; } = true;
}

/// <summary>"Sessize Alınan Konular" — hard filter, never softened by ranking.</summary>
public class UserMutedTopic : BaseEntity
{
    public Guid UserProfileId { get; set; }

    public UserProfile? UserProfile { get; set; }

    public Guid TopicId { get; set; }

    public Topic? Topic { get; set; }

    public DateTimeOffset MutedAt { get; set; }
}

public class UserFavoriteSource : BaseEntity
{
    public Guid UserProfileId { get; set; }

    public UserProfile? UserProfile { get; set; }

    public Guid SourceId { get; set; }

    public Source? Source { get; set; }
}
