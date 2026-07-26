using FocusAI.Domain.Entities.Ai;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Entities.Digests;
using FocusAI.Domain.Entities.Gamification;
using FocusAI.Domain.Entities.Learning;
using FocusAI.Domain.Entities.Search;
using FocusAI.Domain.Entities.Trends;
using FocusAI.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace FocusAI.Application.Common.Interfaces;

/// <summary>
/// Persistence port. Handlers depend on this rather than the concrete
/// DbContext, which keeps the Application layer free of Npgsql/pgvector.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<Source> Sources { get; }

    DbSet<Article> Articles { get; }

    DbSet<Story> Stories { get; }

    DbSet<StorySummary> StorySummaries { get; }

    DbSet<StoryCommitment> StoryCommitments { get; }

    DbSet<StoryAnalysis> StoryAnalyses { get; }

    DbSet<StoryTrust> StoryTrusts { get; }

    DbSet<StoryLink> StoryLinks { get; }

    DbSet<Topic> Topics { get; }

    DbSet<StoryTopic> StoryTopics { get; }

    DbSet<User> Users { get; }

    DbSet<RefreshToken> RefreshTokens { get; }

    DbSet<UserProfile> UserProfiles { get; }

    DbSet<UserInterest> UserInterests { get; }

    DbSet<UserMutedTopic> UserMutedTopics { get; }

    DbSet<UserFavoriteSource> UserFavoriteSources { get; }

    DbSet<NotificationSettings> NotificationSettings { get; }

    DbSet<Domain.Entities.Users.PushSubscription> PushSubscriptions { get; }

    DbSet<Bookmark> Bookmarks { get; }

    DbSet<Interaction> Interactions { get; }

    DbSet<Digest> Digests { get; }

    DbSet<DigestItem> DigestItems { get; }

    DbSet<LearningSuggestion> LearningSuggestions { get; }

    DbSet<LearningResource> LearningResources { get; }

    DbSet<TrendSnapshot> TrendSnapshots { get; }

    DbSet<UserStreak> UserStreaks { get; }

    DbSet<Badge> Badges { get; }

    DbSet<UserBadge> UserBadges { get; }

    DbSet<AiUsageLog> AiUsageLogs { get; }

    DbSet<SearchLog> SearchLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
