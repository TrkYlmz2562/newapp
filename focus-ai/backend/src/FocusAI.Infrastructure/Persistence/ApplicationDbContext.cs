using System.Reflection;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Entities.Ai;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Entities.Digests;
using FocusAI.Domain.Entities.Gamification;
using FocusAI.Domain.Entities.Learning;
using FocusAI.Domain.Entities.Search;
using FocusAI.Domain.Entities.Trends;
using FocusAI.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FocusAI.Infrastructure.Persistence;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options), IApplicationDbContext
{
    public DbSet<Source> Sources => Set<Source>();

    public DbSet<Article> Articles => Set<Article>();

    public DbSet<Story> Stories => Set<Story>();

    public DbSet<StorySummary> StorySummaries => Set<StorySummary>();

    public DbSet<StoryCommitment> StoryCommitments => Set<StoryCommitment>();

    public DbSet<StoryComparison> StoryComparisons => Set<StoryComparison>();

    public DbSet<StoryAnalysis> StoryAnalyses => Set<StoryAnalysis>();

    public DbSet<StoryTrust> StoryTrusts => Set<StoryTrust>();

    public DbSet<StoryLink> StoryLinks => Set<StoryLink>();

    public DbSet<Topic> Topics => Set<Topic>();

    public DbSet<StoryTopic> StoryTopics => Set<StoryTopic>();

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<UserProfile> UserProfiles => Set<UserProfile>();

    public DbSet<UserInterest> UserInterests => Set<UserInterest>();

    public DbSet<UserMutedTopic> UserMutedTopics => Set<UserMutedTopic>();

    public DbSet<UserFavoriteSource> UserFavoriteSources => Set<UserFavoriteSource>();

    public DbSet<NotificationSettings> NotificationSettings => Set<NotificationSettings>();

    public DbSet<Domain.Entities.Users.PushSubscription> PushSubscriptions =>
        Set<Domain.Entities.Users.PushSubscription>();

    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    public DbSet<Interaction> Interactions => Set<Interaction>();

    public DbSet<Digest> Digests => Set<Digest>();

    public DbSet<DigestItem> DigestItems => Set<DigestItem>();

    public DbSet<LearningSuggestion> LearningSuggestions => Set<LearningSuggestion>();

    public DbSet<LearningResource> LearningResources => Set<LearningResource>();

    public DbSet<TrendSnapshot> TrendSnapshots => Set<TrendSnapshot>();

    public DbSet<UserStreak> UserStreaks => Set<UserStreak>();

    public DbSet<Badge> Badges => Set<Badge>();

    public DbSet<UserBadge> UserBadges => Set<UserBadge>();

    public DbSet<AiUsageLog> AiUsageLogs => Set<AiUsageLog>();

    public DbSet<SearchLog> SearchLogs => Set<SearchLog>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.HasPostgresExtension("vector");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        ForceUtcTimestamps(builder);

        base.OnModelCreating(builder);
    }

    /// <summary>
    /// Coerces every <see cref="DateTimeOffset"/> to UTC on the way to the
    /// database.
    /// </summary>
    /// <remarks>
    /// Npgsql maps <c>DateTimeOffset</c> to <c>timestamptz</c> and rejects any
    /// non-zero offset outright. Real RSS feeds publish local offsets constantly
    /// ("-07:00" from a US vendor blog), so without this every ingestion cycle
    /// dies on the first such item. Applying it as a model-wide convention means
    /// no future entity or handler can reintroduce the bug.
    /// </remarks>
    private static void ForceUtcTimestamps(ModelBuilder builder)
    {
        var toUtc = new ValueConverter<DateTimeOffset, DateTimeOffset>(
            value => value.ToUniversalTime(),
            value => value);

        var nullableToUtc = new ValueConverter<DateTimeOffset?, DateTimeOffset?>(
            value => value.HasValue ? value.Value.ToUniversalTime() : null,
            value => value);

        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(toUtc);
                }
                else if (property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(nullableToUtc);
                }
            }
        }
    }
}
