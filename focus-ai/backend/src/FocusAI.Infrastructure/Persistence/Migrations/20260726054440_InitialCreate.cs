using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace FocusAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "ai_usage_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<int>(type: "integer", nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Operation = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PromptTokens = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    Error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ai_usage_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "badges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_badges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "search_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RawQuery = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    ParsedFilterJson = table.Column<string>(type: "jsonb", nullable: true),
                    ResultCount = table.Column<int>(type: "integer", nullable: false),
                    LatencyMs = table.Column<int>(type: "integer", nullable: false),
                    UsedLlmParsing = table.Column<bool>(type: "boolean", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_search_logs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    WebsiteUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    FeedUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    DefaultContentCategory = table.Column<int>(type: "integer", nullable: false),
                    IsOfficial = table.Column<bool>(type: "boolean", nullable: false),
                    TrustWeight = table.Column<double>(type: "double precision", nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    FetchIntervalMinutes = table.Column<int>(type: "integer", nullable: false),
                    LastFetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastSucceededAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ETag = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LastModified = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IconUrl = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Dek = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastActivityAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PrimaryArticleId = table.Column<Guid>(type: "uuid", nullable: true),
                    HeroImageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    SourceCount = table.Column<int>(type: "integer", nullable: false),
                    OfficialSourceCount = table.Column<int>(type: "integer", nullable: false),
                    TrustScore = table.Column<int>(type: "integer", nullable: false),
                    ImportanceScore = table.Column<int>(type: "integer", nullable: false),
                    EngagementScore = table.Column<int>(type: "integer", nullable: false),
                    ReadingMinutes = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<int>(type: "integer", nullable: false),
                    Aliases = table.Column<List<string>>(type: "text[]", nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsSuggestable = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_topics", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    NormalizedEmail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Plan = table.Column<int>(type: "integer", nullable: false),
                    TimeZone = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Locale = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Roles = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "articles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalId = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    CanonicalUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Excerpt = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Content = table.Column<string>(type: "text", nullable: true),
                    ImageUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    ContentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SimHash = table.Column<long>(type: "bigint", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(1536)", nullable: true),
                    EngagementScore = table.Column<int>(type: "integer", nullable: true),
                    CommentCount = table.Column<int>(type: "integer", nullable: true),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsPrimary = table.Column<bool>(type: "boolean", nullable: false),
                    FailureReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_articles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_articles_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_articles_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "story_analyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    WhyImportant = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    RealImpact = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Hype = table.Column<int>(type: "integer", nullable: false),
                    HypeReasoning = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    LearnUrgency = table.Column<int>(type: "integer", nullable: false),
                    Longevity = table.Column<int>(type: "integer", nullable: false),
                    LongevityReasoning = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    StackNotes = table.Column<string>(type: "jsonb", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_analyses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_analyses_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "story_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_links_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "story_summaries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Summary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    WhyItMatters = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WhoIsAffected = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    WhatShouldIDo = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    KeyPoints = table.Column<List<string>>(type: "text[]", nullable: false),
                    ExtendedSummary = table.Column<string>(type: "character varying(12000)", maxLength: 12000, nullable: true),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PromptTokens = table.Column<int>(type: "integer", nullable: false),
                    CompletionTokens = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_summaries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_summaries_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "story_trust",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficialSourceScore = table.Column<int>(type: "integer", nullable: false),
                    CorroborationScore = table.Column<int>(type: "integer", nullable: false),
                    RecencyScore = table.Column<int>(type: "integer", nullable: false),
                    TechnicalAccuracyScore = table.Column<int>(type: "integer", nullable: false),
                    CommunityScore = table.Column<int>(type: "integer", nullable: false),
                    Total = table.Column<int>(type: "integer", nullable: false),
                    Explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_trust", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_trust_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "story_topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_topics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_topics_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_story_topics_topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "trend_snapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Period = table.Column<int>(type: "integer", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "date", nullable: false),
                    StoryCount = table.Column<int>(type: "integer", nullable: false),
                    SourceCount = table.Column<int>(type: "integer", nullable: false),
                    WeightedImportance = table.Column<int>(type: "integer", nullable: false),
                    MomentumPercent = table.Column<double>(type: "double precision", nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trend_snapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_trend_snapshots_topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "bookmarks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Tags = table.Column<List<string>>(type: "text[]", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_bookmarks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_bookmarks_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_bookmarks_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "digests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Period = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Intro = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ReadingMinutes = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_digests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_digests_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "interactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    DwellSeconds = table.Column<int>(type: "integer", nullable: true),
                    Surface = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_interactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_interactions_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_interactions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learning_suggestions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SourceStoryId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_suggestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_suggestions_topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_learning_suggestions_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "notification_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    MorningDigest = table.Column<bool>(type: "boolean", nullable: false),
                    EveningDigest = table.Column<bool>(type: "boolean", nullable: false),
                    BigNewsOnly = table.Column<bool>(type: "boolean", nullable: false),
                    WeeklyDigest = table.Column<bool>(type: "boolean", nullable: false),
                    BigNewsThreshold = table.Column<int>(type: "integer", nullable: false),
                    QuietHoursStart = table.Column<int>(type: "integer", nullable: false),
                    QuietHoursEnd = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_notification_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_notification_settings_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacedByTokenId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedByIp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_refresh_tokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_refresh_tokens_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Headline = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ExperienceLevel = table.Column<int>(type: "integer", nullable: false),
                    DailyDigestHour = table.Column<int>(type: "integer", nullable: false),
                    DailyStoryCount = table.Column<int>(type: "integer", nullable: false),
                    DailyLearningMinutes = table.Column<int>(type: "integer", nullable: false),
                    OnboardingCompleted = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_profiles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_streaks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentStreak = table.Column<int>(type: "integer", nullable: false),
                    LongestStreak = table.Column<int>(type: "integer", nullable: false),
                    LastActiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    WeeklyGoalDays = table.Column<int>(type: "integer", nullable: false),
                    CompletedLearnings = table.Column<int>(type: "integer", nullable: false),
                    TotalStoriesRead = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_streaks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_streaks_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "digest_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DigestId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_digest_items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_digest_items_digests_DigestId",
                        column: x => x.DigestId,
                        principalTable: "digests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_digest_items_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learning_resources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningSuggestionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    EstimatedMinutes = table.Column<int>(type: "integer", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_resources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_resources_learning_suggestions_LearningSuggestionId",
                        column: x => x.LearningSuggestionId,
                        principalTable: "learning_suggestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "push_subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NotificationSettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Auth = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    UserAgent = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_push_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_push_subscriptions_notification_settings_NotificationSettin~",
                        column: x => x.NotificationSettingsId,
                        principalTable: "notification_settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_favorite_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_favorite_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_favorite_sources_sources_SourceId",
                        column: x => x.SourceId,
                        principalTable: "sources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_favorite_sources_user_profiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "user_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_interests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    Weight = table.Column<double>(type: "double precision", nullable: false),
                    IsExplicit = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_interests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_interests_topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_interests_user_profiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "user_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_muted_topics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    TopicId = table.Column<Guid>(type: "uuid", nullable: false),
                    MutedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_muted_topics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_muted_topics_topics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "topics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_muted_topics_user_profiles_UserProfileId",
                        column: x => x.UserProfileId,
                        principalTable: "user_profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_badges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserStreakId = table.Column<Guid>(type: "uuid", nullable: false),
                    BadgeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AwardedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_badges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_user_badges_badges_BadgeId",
                        column: x => x.BadgeId,
                        principalTable: "badges",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_user_badges_user_streaks_UserStreakId",
                        column: x => x.UserStreakId,
                        principalTable: "user_streaks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ai_usage_logs_Operation_OccurredAt",
                table: "ai_usage_logs",
                columns: new[] { "Operation", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ai_usage_logs_UserId_OccurredAt",
                table: "ai_usage_logs",
                columns: new[] { "UserId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_articles_CanonicalUrl",
                table: "articles",
                column: "CanonicalUrl");

            migrationBuilder.CreateIndex(
                name: "IX_articles_ContentHash",
                table: "articles",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_articles_SourceId_ExternalId",
                table: "articles",
                columns: new[] { "SourceId", "ExternalId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_articles_Status_PublishedAt",
                table: "articles",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_articles_StoryId",
                table: "articles",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_badges_Slug",
                table: "badges",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_bookmarks_StoryId",
                table: "bookmarks",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_bookmarks_UserId_CreatedAt",
                table: "bookmarks",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_bookmarks_UserId_StoryId",
                table: "bookmarks",
                columns: new[] { "UserId", "StoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_digest_items_DigestId_Rank",
                table: "digest_items",
                columns: new[] { "DigestId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_digest_items_StoryId",
                table: "digest_items",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_digests_UserId_Date_Period",
                table: "digests",
                columns: new[] { "UserId", "Date", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_interactions_StoryId_Type",
                table: "interactions",
                columns: new[] { "StoryId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_interactions_UserId_Type_OccurredAt",
                table: "interactions",
                columns: new[] { "UserId", "Type", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_learning_resources_LearningSuggestionId",
                table: "learning_resources",
                column: "LearningSuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_suggestions_TopicId",
                table: "learning_suggestions",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_suggestions_UserId_Date",
                table: "learning_suggestions",
                columns: new[] { "UserId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_notification_settings_UserId",
                table: "notification_settings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_Endpoint",
                table: "push_subscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_push_subscriptions_NotificationSettingsId",
                table: "push_subscriptions",
                column: "NotificationSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_TokenHash",
                table: "refresh_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_refresh_tokens_UserId_RevokedAt",
                table: "refresh_tokens",
                columns: new[] { "UserId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_search_logs_OccurredAt",
                table: "search_logs",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_sources_IsEnabled_LastFetchedAt",
                table: "sources",
                columns: new[] { "IsEnabled", "LastFetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_sources_Slug",
                table: "sources",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stories_Category",
                table: "stories",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_stories_LastActivityAt",
                table: "stories",
                column: "LastActivityAt");

            migrationBuilder.CreateIndex(
                name: "IX_stories_Slug",
                table: "stories",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stories_Status_ImportanceScore_PublishedAt",
                table: "stories",
                columns: new[] { "Status", "ImportanceScore", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stories_Status_PublishedAt",
                table: "stories",
                columns: new[] { "Status", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_story_analyses_StoryId",
                table: "story_analyses",
                column: "StoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_analyses_StoryId_Language",
                table: "story_analyses",
                columns: new[] { "StoryId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_links_StoryId_Position",
                table: "story_links",
                columns: new[] { "StoryId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_story_summaries_StoryId",
                table: "story_summaries",
                column: "StoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_summaries_StoryId_Language",
                table: "story_summaries",
                columns: new[] { "StoryId", "Language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_topics_StoryId_TopicId",
                table: "story_topics",
                columns: new[] { "StoryId", "TopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_topics_TopicId",
                table: "story_topics",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_story_trust_StoryId",
                table: "story_trust",
                column: "StoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_topics_Slug",
                table: "topics",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trend_snapshots_Period_PeriodStart_TopicId",
                table: "trend_snapshots",
                columns: new[] { "Period", "PeriodStart", "TopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_trend_snapshots_TopicId",
                table: "trend_snapshots",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_user_badges_BadgeId",
                table: "user_badges",
                column: "BadgeId");

            migrationBuilder.CreateIndex(
                name: "IX_user_badges_UserStreakId_BadgeId",
                table: "user_badges",
                columns: new[] { "UserStreakId", "BadgeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_favorite_sources_SourceId",
                table: "user_favorite_sources",
                column: "SourceId");

            migrationBuilder.CreateIndex(
                name: "IX_user_favorite_sources_UserProfileId_SourceId",
                table: "user_favorite_sources",
                columns: new[] { "UserProfileId", "SourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_interests_TopicId",
                table: "user_interests",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_user_interests_UserProfileId_TopicId",
                table: "user_interests",
                columns: new[] { "UserProfileId", "TopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_muted_topics_TopicId",
                table: "user_muted_topics",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_user_muted_topics_UserProfileId_TopicId",
                table: "user_muted_topics",
                columns: new[] { "UserProfileId", "TopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_profiles_UserId",
                table: "user_profiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_streaks_UserId",
                table: "user_streaks",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_NormalizedEmail",
                table: "users",
                column: "NormalizedEmail",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ai_usage_logs");

            migrationBuilder.DropTable(
                name: "articles");

            migrationBuilder.DropTable(
                name: "bookmarks");

            migrationBuilder.DropTable(
                name: "digest_items");

            migrationBuilder.DropTable(
                name: "interactions");

            migrationBuilder.DropTable(
                name: "learning_resources");

            migrationBuilder.DropTable(
                name: "push_subscriptions");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "search_logs");

            migrationBuilder.DropTable(
                name: "story_analyses");

            migrationBuilder.DropTable(
                name: "story_links");

            migrationBuilder.DropTable(
                name: "story_summaries");

            migrationBuilder.DropTable(
                name: "story_topics");

            migrationBuilder.DropTable(
                name: "story_trust");

            migrationBuilder.DropTable(
                name: "trend_snapshots");

            migrationBuilder.DropTable(
                name: "user_badges");

            migrationBuilder.DropTable(
                name: "user_favorite_sources");

            migrationBuilder.DropTable(
                name: "user_interests");

            migrationBuilder.DropTable(
                name: "user_muted_topics");

            migrationBuilder.DropTable(
                name: "digests");

            migrationBuilder.DropTable(
                name: "learning_suggestions");

            migrationBuilder.DropTable(
                name: "notification_settings");

            migrationBuilder.DropTable(
                name: "stories");

            migrationBuilder.DropTable(
                name: "badges");

            migrationBuilder.DropTable(
                name: "user_streaks");

            migrationBuilder.DropTable(
                name: "sources");

            migrationBuilder.DropTable(
                name: "user_profiles");

            migrationBuilder.DropTable(
                name: "topics");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
