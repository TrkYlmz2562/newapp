using System.Text.Json;
using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FocusAI.Infrastructure.Persistence.Configurations;

public class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.ToTable("sources");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.HasIndex(x => new { x.IsEnabled, x.LastFetchedAt });

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.WebsiteUrl).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.FeedUrl).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(10);
        builder.Property(x => x.ETag).HasMaxLength(500);
        builder.Property(x => x.LastModified).HasMaxLength(200);
        builder.Property(x => x.LastError).HasMaxLength(1000);
        builder.Property(x => x.IconUrl).HasMaxLength(1000);

        builder.HasMany(x => x.Articles)
            .WithOne(x => x.Source)
            .HasForeignKey(x => x.SourceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class ArticleConfiguration : IEntityTypeConfiguration<Article>
{
    public void Configure(EntityTypeBuilder<Article> builder)
    {
        builder.ToTable("articles");
        builder.HasKey(x => x.Id);

        // Idempotent re-ingest: the same feed entry must never land twice.
        builder.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique();
        builder.HasIndex(x => x.CanonicalUrl);
        builder.HasIndex(x => x.ContentHash);
        builder.HasIndex(x => new { x.Status, x.PublishedAt });
        builder.HasIndex(x => x.StoryId);

        builder.Property(x => x.ExternalId).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Url).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.CanonicalUrl).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Author).HasMaxLength(200);
        builder.Property(x => x.Excerpt).HasMaxLength(4000);
        builder.Property(x => x.ImageUrl).HasMaxLength(2000);
        builder.Property(x => x.Language).HasMaxLength(10);
        builder.Property(x => x.ContentHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.FailureReason).HasMaxLength(1000);

        builder.Property(x => x.Embedding)
            .HasColumnType(EmbeddingConfig.ColumnType)
            .HasConversion(EmbeddingConfig.Converter, EmbeddingConfig.Comparer);
    }
}

public class StoryConfiguration : IEntityTypeConfiguration<Story>
{
    public void Configure(EntityTypeBuilder<Story> builder)
    {
        builder.ToTable("stories");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Slug).IsUnique();

        // The feed's hot path: published stories ordered by importance.
        builder.HasIndex(x => new { x.Status, x.ImportanceScore, x.PublishedAt });
        builder.HasIndex(x => new { x.Status, x.PublishedAt });
        builder.HasIndex(x => x.LastActivityAt);
        builder.HasIndex(x => x.Category);

        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(FieldLimits.StoryTitle).IsRequired();
        builder.Property(x => x.Dek).HasMaxLength(FieldLimits.StoryDek);
        builder.Property(x => x.HeroImageUrl).HasMaxLength(2000);

        builder.Property(x => x.Embedding)
            .HasColumnType(EmbeddingConfig.ColumnType)
            .HasConversion(EmbeddingConfig.Converter, EmbeddingConfig.Comparer);

        builder.HasMany(x => x.Articles)
            .WithOne(x => x.Story)
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(x => x.Summary)
            .WithOne(x => x.Story)
            .HasForeignKey<StorySummary>(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Analysis)
            .WithOne(x => x.Story)
            .HasForeignKey<StoryAnalysis>(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Trust)
            .WithOne(x => x.Story)
            .HasForeignKey<StoryTrust>(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Commitment)
            .WithOne(x => x.Story)
            .HasForeignKey<StoryCommitment>(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.Comparison)
            .WithOne(x => x.Story)
            .HasForeignKey<StoryComparison>(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Links)
            .WithOne(x => x.Story)
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Topics)
            .WithOne(x => x.Story)
            .HasForeignKey(x => x.StoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class StorySummaryConfiguration : IEntityTypeConfiguration<StorySummary>
{
    public void Configure(EntityTypeBuilder<StorySummary> builder)
    {
        builder.ToTable("story_summaries");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.StoryId, x.Language }).IsUnique();

        builder.Property(x => x.Language).HasMaxLength(10);
        builder.Property(x => x.Summary).HasMaxLength(FieldLimits.Summary).IsRequired();
        builder.Property(x => x.WhyItMatters).HasMaxLength(FieldLimits.SummarySection);
        builder.Property(x => x.WhoIsAffected).HasMaxLength(FieldLimits.SummarySection);
        builder.Property(x => x.WhatShouldIDo).HasMaxLength(FieldLimits.SummarySection);
        builder.Property(x => x.ExtendedSummary).HasMaxLength(FieldLimits.ExtendedSummary);
        builder.Property(x => x.VisualEntity).HasMaxLength(FieldLimits.VisualEntity);
        builder.Property(x => x.Provider).HasMaxLength(FieldLimits.ProviderName);
        builder.Property(x => x.Model).HasMaxLength(FieldLimits.ModelName);

        // Npgsql maps List<string> onto text[] natively — no JSON round-trip needed.
        builder.Property(x => x.KeyPoints).HasColumnType("text[]");
    }
}

public class StoryCommitmentConfiguration : IEntityTypeConfiguration<StoryCommitment>
{
    public void Configure(EntityTypeBuilder<StoryCommitment> builder)
    {
        builder.ToTable("story_commitments");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.StoryId).IsUnique();

        // The Finans feed's hot path: publishable tiers ordered by event date.
        builder.HasIndex(x => new { x.Tier, x.Horizon, x.EventDate });

        builder.Property(x => x.Event).HasMaxLength(FieldLimits.CommitmentEvent);
        builder.Property(x => x.DateText).HasMaxLength(FieldLimits.CommitmentDateText);
        builder.Property(x => x.Quote).HasMaxLength(FieldLimits.CommitmentQuote);
        builder.Property(x => x.Condition).HasMaxLength(FieldLimits.CommitmentQuote);
        builder.Property(x => x.Reference).HasMaxLength(FieldLimits.CommitmentReference);
    }
}

public class StoryComparisonConfiguration : IEntityTypeConfiguration<StoryComparison>
{
    public void Configure(EntityTypeBuilder<StoryComparison> builder)
    {
        builder.ToTable("story_comparisons");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.StoryId).IsUnique();

        builder.Property(x => x.Provider).HasMaxLength(FieldLimits.ProviderName);
        builder.Property(x => x.Model).HasMaxLength(FieldLimits.ModelName);

        // A short, read-only list rendered as a block — jsonb keeps it one row
        // instead of a child table whose shape would change with the prompt.
        builder.Property(x => x.Points)
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<List<ComparisonPoint>>(value, (JsonSerializerOptions?)null)
                         ?? new List<ComparisonPoint>(),
                new ValueComparer<List<ComparisonPoint>>(
                    (left, right) => JsonSerializer.Serialize(left, (JsonSerializerOptions?)null) ==
                                     JsonSerializer.Serialize(right, (JsonSerializerOptions?)null),
                    value => value == null ? 0 : JsonSerializer.Serialize(value, (JsonSerializerOptions?)null).GetHashCode(),
                    value => JsonSerializer.Deserialize<List<ComparisonPoint>>(
                        JsonSerializer.Serialize(value, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)
                        ?? new List<ComparisonPoint>()));
    }
}

public class StoryAnalysisConfiguration : IEntityTypeConfiguration<StoryAnalysis>
{
    public void Configure(EntityTypeBuilder<StoryAnalysis> builder)
    {
        builder.ToTable("story_analyses");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.StoryId, x.Language }).IsUnique();

        builder.Property(x => x.Language).HasMaxLength(10);
        builder.Property(x => x.WhyImportant).HasMaxLength(FieldLimits.AnalysisSection).IsRequired();
        builder.Property(x => x.RealImpact).HasMaxLength(FieldLimits.AnalysisSection);
        builder.Property(x => x.HypeReasoning).HasMaxLength(FieldLimits.AnalysisSection);
        builder.Property(x => x.LongevityReasoning).HasMaxLength(FieldLimits.AnalysisSection);
        builder.Property(x => x.Provider).HasMaxLength(FieldLimits.ProviderName);
        builder.Property(x => x.Model).HasMaxLength(FieldLimits.ModelName);

        // Stack notes are an open-ended slug → advice map; jsonb keeps it queryable
        // without a table whose shape changes every time a new stack is covered.
        builder.Property(x => x.StackNotes)
            .HasColumnType("jsonb")
            .HasConversion(
                value => JsonSerializer.Serialize(value, (JsonSerializerOptions?)null),
                value => JsonSerializer.Deserialize<Dictionary<string, string>>(value, (JsonSerializerOptions?)null)
                         ?? new Dictionary<string, string>(),
                new ValueComparer<Dictionary<string, string>>(
                    (left, right) => left != null && right != null && left.Count == right.Count &&
                                     !left.Except(right).Any(),
                    value => value.Aggregate(0, (hash, pair) => HashCode.Combine(hash, pair.Key, pair.Value)),
                    value => new Dictionary<string, string>(value)));
    }
}

public class StoryTrustConfiguration : IEntityTypeConfiguration<StoryTrust>
{
    public void Configure(EntityTypeBuilder<StoryTrust> builder)
    {
        builder.ToTable("story_trust");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.StoryId).IsUnique();
        builder.Property(x => x.Explanation).HasMaxLength(500);
    }
}

public class StoryLinkConfiguration : IEntityTypeConfiguration<StoryLink>
{
    public void Configure(EntityTypeBuilder<StoryLink> builder)
    {
        builder.ToTable("story_links");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.StoryId, x.Position });

        builder.Property(x => x.Url).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ThumbnailUrl).HasMaxLength(2000);
    }
}

public class TopicConfiguration : IEntityTypeConfiguration<Topic>
{
    public void Configure(EntityTypeBuilder<Topic> builder)
    {
        builder.ToTable("topics");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => x.Slug).IsUnique();

        builder.Property(x => x.Name).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Aliases).HasColumnType("text[]");
    }
}

public class StoryTopicConfiguration : IEntityTypeConfiguration<StoryTopic>
{
    public void Configure(EntityTypeBuilder<StoryTopic> builder)
    {
        builder.ToTable("story_topics");
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.StoryId, x.TopicId }).IsUnique();
        builder.HasIndex(x => x.TopicId);

        builder.HasOne(x => x.Topic)
            .WithMany(x => x.Stories)
            .HasForeignKey(x => x.TopicId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
