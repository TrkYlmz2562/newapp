using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Ai;
using FocusAI.Domain.Entities.Digests;
using FocusAI.Domain.Entities.Gamification;
using FocusAI.Domain.Entities.Learning;
using FocusAI.Domain.Entities.Search;
using FocusAI.Domain.Entities.Trends;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FocusAI.Infrastructure.Persistence.Configurations;

public class DigestConfiguration : IEntityTypeConfiguration<Digest>
{
    public void Configure(EntityTypeBuilder<Digest> builder)
    {
        builder.ToTable("digests");
        builder.HasKey(x => x.Id);

        // One edition per reader per period per day. The filtered unique index
        // keeps the shared (UserId null) edition distinct from personal ones.
        builder.HasIndex(x => new { x.UserId, x.Date, x.Period }).IsUnique();

        builder.Property(x => x.Intro).HasMaxLength(1000);
        builder.Property(x => x.Language).HasMaxLength(10);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Items)
            .WithOne(x => x.Digest)
            .HasForeignKey(x => x.DigestId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class DigestItemConfiguration : IEntityTypeConfiguration<DigestItem>
{
    public void Configure(EntityTypeBuilder<DigestItem> builder)
    {
        builder.ToTable("digest_items");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.DigestId, x.Rank });

        builder.Property(x => x.Reason).HasMaxLength(300);

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LearningSuggestionConfiguration : IEntityTypeConfiguration<LearningSuggestion>
{
    public void Configure(EntityTypeBuilder<LearningSuggestion> builder)
    {
        builder.ToTable("learning_suggestions");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.Date }).IsUnique();

        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Rationale).HasMaxLength(2000).IsRequired();

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Topic)
            .WithMany()
            .HasForeignKey(x => x.TopicId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Resources)
            .WithOne(x => x.LearningSuggestion)
            .HasForeignKey(x => x.LearningSuggestionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LearningResourceConfiguration : IEntityTypeConfiguration<LearningResource>
{
    public void Configure(EntityTypeBuilder<LearningResource> builder)
    {
        builder.ToTable("learning_resources");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Kind).HasMaxLength(30);
    }
}

public class LearningBriefConfiguration : IEntityTypeConfiguration<LearningBrief>
{
    public void Configure(EntityTypeBuilder<LearningBrief> builder)
    {
        builder.ToTable("learning_briefs");
        builder.HasKey(x => x.Id);

        // The list is read newest-first, filtered to one reader.
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });

        builder.Property(x => x.Title).HasMaxLength(500).IsRequired();
        builder.Property(x => x.LearningGoal).HasMaxLength(500);
        builder.Property(x => x.DemoIdea).HasMaxLength(500);
        builder.Property(x => x.Provider).HasMaxLength(FieldLimits.ProviderName);
        builder.Property(x => x.Model).HasMaxLength(FieldLimits.ModelName);

        // No length cap on the prompt: it grows with the number of sources a story
        // gathered, and truncating it would cut the case file mid-quote — the one
        // part of the brief that has to survive intact.
        builder.Property(x => x.Prompt);

        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Deleting the day's suggestion must not take the lesson with it.
        builder.HasOne(x => x.LearningSuggestion)
            .WithMany()
            .HasForeignKey(x => x.LearningSuggestionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasMany(x => x.Stories)
            .WithOne(x => x.LearningBrief)
            .HasForeignKey(x => x.LearningBriefId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class LearningBriefStoryConfiguration : IEntityTypeConfiguration<LearningBriefStory>
{
    public void Configure(EntityTypeBuilder<LearningBriefStory> builder)
    {
        builder.ToTable("learning_brief_stories");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.LearningBriefId, x.StoryId }).IsUnique();

        // Finding "do I already have a lesson queued for this story" is the check
        // that runs on every press of the button.
        builder.HasIndex(x => x.StoryId);

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TrendSnapshotConfiguration : IEntityTypeConfiguration<TrendSnapshot>
{
    public void Configure(EntityTypeBuilder<TrendSnapshot> builder)
    {
        builder.ToTable("trend_snapshots");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.Period, x.PeriodStart, x.TopicId }).IsUnique();

        builder.HasOne(x => x.Topic)
            .WithMany()
            .HasForeignKey(x => x.TopicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserStreakConfiguration : IEntityTypeConfiguration<UserStreak>
{
    public void Configure(EntityTypeBuilder<UserStreak> builder)
    {
        builder.ToTable("user_streaks");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.UserId).IsUnique();

        builder.HasMany(x => x.Badges)
            .WithOne(x => x.UserStreak)
            .HasForeignKey(x => x.UserStreakId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class BadgeConfiguration : IEntityTypeConfiguration<Badge>
{
    public void Configure(EntityTypeBuilder<Badge> builder)
    {
        builder.ToTable("badges");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Slug).IsUnique();

        builder.Property(x => x.Slug).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(400).IsRequired();
        builder.Property(x => x.Emoji).HasMaxLength(16);
    }
}

public class UserBadgeConfiguration : IEntityTypeConfiguration<UserBadge>
{
    public void Configure(EntityTypeBuilder<UserBadge> builder)
    {
        builder.ToTable("user_badges");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserStreakId, x.BadgeId }).IsUnique();

        builder.HasOne(x => x.Badge)
            .WithMany()
            .HasForeignKey(x => x.BadgeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class AiUsageLogConfiguration : IEntityTypeConfiguration<AiUsageLog>
{
    public void Configure(EntityTypeBuilder<AiUsageLog> builder)
    {
        builder.ToTable("ai_usage_logs");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.OccurredAt });
        builder.HasIndex(x => new { x.Operation, x.OccurredAt });

        builder.Property(x => x.Model).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Operation).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Error).HasMaxLength(1000);
    }
}

public class SearchLogConfiguration : IEntityTypeConfiguration<SearchLog>
{
    public void Configure(EntityTypeBuilder<SearchLog> builder)
    {
        builder.ToTable("search_logs");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.OccurredAt);

        builder.Property(x => x.RawQuery).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ParsedFilterJson).HasColumnType("jsonb");
    }
}
