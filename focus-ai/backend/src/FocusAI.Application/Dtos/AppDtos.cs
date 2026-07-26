using FocusAI.Domain.Enums;

namespace FocusAI.Application.Dtos;

public sealed record AuthResultDto(
    string AccessToken,
    string RefreshToken,
    DateTimeOffset AccessTokenExpiresAt,
    UserDto User);

public sealed record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    SubscriptionPlan Plan,
    string TimeZone,
    string Locale,
    IReadOnlyList<string> Roles,
    bool OnboardingCompleted);

public sealed record ProfileDto(
    Guid UserId,
    string DisplayName,
    string Email,
    string? Headline,
    ExperienceLevel ExperienceLevel,
    int DailyDigestHour,
    int DailyStoryCount,
    int DailyLearningMinutes,
    bool OnboardingCompleted,
    SubscriptionPlan Plan,
    IReadOnlyList<InterestDto> Interests,
    IReadOnlyList<TopicDto> MutedTopics,
    IReadOnlyList<FavoriteSourceDto> FavoriteSources,
    NotificationSettingsDto Notifications,
    StreakDto Streak);

public sealed record InterestDto(Guid TopicId, string Name, string Slug, double Weight, bool IsExplicit);

public sealed record FavoriteSourceDto(Guid SourceId, string Name, string Slug, string? IconUrl);

public sealed record NotificationSettingsDto(
    bool MorningDigest,
    bool EveningDigest,
    bool BigNewsOnly,
    bool WeeklyDigest,
    int BigNewsThreshold,
    int QuietHoursStart,
    int QuietHoursEnd);

public sealed record StreakDto(
    int CurrentStreak,
    int LongestStreak,
    int WeeklyGoalDays,
    int CompletedLearnings,
    int TotalStoriesRead,
    DateOnly? LastActiveDate,
    IReadOnlyList<BadgeDto> Badges);

public sealed record BadgeDto(string Slug, string Name, string Description, string? Emoji, DateTimeOffset AwardedAt);

public sealed record DigestDto(
    Guid Id,
    DateOnly Date,
    DigestPeriod Period,
    string? Intro,
    int ReadingMinutes,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<DigestEntryDto> Items);

public sealed record DigestEntryDto(int Rank, string? Reason, StoryCardDto Story);

public sealed record BookmarkDto(
    Guid Id,
    string? Note,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAt,
    StoryCardDto Story);

public sealed record LearningSuggestionDto(
    Guid Id,
    DateOnly Date,
    string Title,
    string Rationale,
    int EstimatedMinutes,
    LearningStatus Status,
    TopicDto? Topic,
    IReadOnlyList<LearningResourceDto> Resources);

public sealed record LearningResourceDto(string Title, string Url, string Kind, int EstimatedMinutes);

public sealed record TrendPointDto(DateOnly PeriodStart, int StoryCount, int WeightedImportance);

public sealed record TrendDto(
    Guid TopicId,
    string Name,
    string Slug,
    int StoryCount,
    int SourceCount,
    int WeightedImportance,
    double MomentumPercent,
    IReadOnlyList<TrendPointDto> History);

public sealed record SourceDto(
    Guid Id,
    string Name,
    string Slug,
    string WebsiteUrl,
    SourceKind Kind,
    SourceCategory Category,
    bool IsOfficial,
    bool IsEnabled,
    string? IconUrl,
    DateTimeOffset? LastSucceededAt,
    int ConsecutiveFailures);

public sealed record AskResultDto(
    string Answer,
    double Confidence,
    IReadOnlyList<StoryCardDto> Citations);

/// <summary>Outcome of one ingestion cycle, surfaced on the admin/ops endpoint.</summary>
/// <summary>Result of the one-off image backfill over already-ingested articles.</summary>
public sealed record ImageBackfillReportDto(
    int ArticlesScanned,
    int ImagesFound,
    int NoImageOnPage,
    int StoriesUpdated,
    int ArticlesRemaining);

public sealed record IngestionReportDto(
    int SourcesPolled,
    int ItemsFetched,
    int ArticlesCreated,
    int DuplicatesMerged,
    int StoriesCreated,
    int Failures,
    IReadOnlyList<string> Errors);
