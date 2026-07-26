using FocusAI.Domain.Entities.Gamification;
using FocusAI.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FocusAI.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.NormalizedEmail).IsUnique();

        builder.Property(x => x.Email).HasMaxLength(256).IsRequired();
        builder.Property(x => x.NormalizedEmail).HasMaxLength(256).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(80).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(x => x.TimeZone).HasMaxLength(60);
        builder.Property(x => x.Locale).HasMaxLength(10);
        builder.Property(x => x.Roles).HasColumnType("text[]");

        builder.HasOne(x => x.Profile)
            .WithOne(x => x.User)
            .HasForeignKey<UserProfile>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.NotificationSettings)
            .WithOne(x => x.User)
            .HasForeignKey<NotificationSettings>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Streak)
            .WithOne(x => x.User)
            .HasForeignKey<UserStreak>(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.RefreshTokens)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Bookmarks)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Interactions)
            .WithOne(x => x.User)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.RevokedAt });

        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedByIp).HasMaxLength(64);
    }
}

public class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("user_profiles");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.UserId).IsUnique();
        builder.Property(x => x.Headline).HasMaxLength(200);

        builder.HasMany(x => x.Interests)
            .WithOne(x => x.UserProfile)
            .HasForeignKey(x => x.UserProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.MutedTopics)
            .WithOne(x => x.UserProfile)
            .HasForeignKey(x => x.UserProfileId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.FavoriteSources)
            .WithOne(x => x.UserProfile)
            .HasForeignKey(x => x.UserProfileId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserInterestConfiguration : IEntityTypeConfiguration<UserInterest>
{
    public void Configure(EntityTypeBuilder<UserInterest> builder)
    {
        builder.ToTable("user_interests");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserProfileId, x.TopicId }).IsUnique();

        builder.HasOne(x => x.Topic)
            .WithMany()
            .HasForeignKey(x => x.TopicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserMutedTopicConfiguration : IEntityTypeConfiguration<UserMutedTopic>
{
    public void Configure(EntityTypeBuilder<UserMutedTopic> builder)
    {
        builder.ToTable("user_muted_topics");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserProfileId, x.TopicId }).IsUnique();

        builder.HasOne(x => x.Topic)
            .WithMany()
            .HasForeignKey(x => x.TopicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class UserFavoriteSourceConfiguration : IEntityTypeConfiguration<UserFavoriteSource>
{
    public void Configure(EntityTypeBuilder<UserFavoriteSource> builder)
    {
        builder.ToTable("user_favorite_sources");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserProfileId, x.SourceId }).IsUnique();

        builder.HasOne(x => x.Source)
            .WithMany()
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class NotificationSettingsConfiguration : IEntityTypeConfiguration<NotificationSettings>
{
    public void Configure(EntityTypeBuilder<NotificationSettings> builder)
    {
        builder.ToTable("notification_settings");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.UserId).IsUnique();

        builder.HasMany(x => x.PushSubscriptions)
            .WithOne(x => x.NotificationSettings)
            .HasForeignKey(x => x.NotificationSettingsId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class PushSubscriptionConfiguration : IEntityTypeConfiguration<Domain.Entities.Users.PushSubscription>
{
    public void Configure(EntityTypeBuilder<Domain.Entities.Users.PushSubscription> builder)
    {
        builder.ToTable("push_subscriptions");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Endpoint).IsUnique();

        builder.Property(x => x.Endpoint).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.P256dh).HasMaxLength(300).IsRequired();
        builder.Property(x => x.Auth).HasMaxLength(300).IsRequired();
        builder.Property(x => x.UserAgent).HasMaxLength(500);
    }
}

public class BookmarkConfiguration : IEntityTypeConfiguration<Bookmark>
{
    public void Configure(EntityTypeBuilder<Bookmark> builder)
    {
        builder.ToTable("bookmarks");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.UserId, x.StoryId }).IsUnique();
        builder.HasIndex(x => new { x.UserId, x.CreatedAt });

        builder.Property(x => x.Note).HasMaxLength(2000);
        builder.Property(x => x.Tags).HasColumnType("text[]");

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class InteractionConfiguration : IEntityTypeConfiguration<Interaction>
{
    public void Configure(EntityTypeBuilder<Interaction> builder)
    {
        builder.ToTable("interactions");
        builder.HasKey(x => x.Id);

        // Personalisation reads this by (user, type, time desc) on every feed request.
        builder.HasIndex(x => new { x.UserId, x.Type, x.OccurredAt });
        builder.HasIndex(x => new { x.StoryId, x.Type });

        builder.Property(x => x.Surface).HasMaxLength(40);

        builder.HasOne(x => x.Story)
            .WithMany()
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
