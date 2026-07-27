using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLearningBriefs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "learning_briefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Origin = table.Column<int>(type: "integer", nullable: false),
                    LearningSuggestionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Prompt = table.Column<string>(type: "text", nullable: true),
                    LearningGoal = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    EntryLevel = table.Column<int>(type: "integer", nullable: true),
                    DemoIdea = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PlannerUnavailable = table.Column<bool>(type: "boolean", nullable: false),
                    PersonaVersion = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    GeneratedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_briefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_briefs_learning_suggestions_LearningSuggestionId",
                        column: x => x.LearningSuggestionId,
                        principalTable: "learning_suggestions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_learning_briefs_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "learning_brief_stories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LearningBriefId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_learning_brief_stories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_learning_brief_stories_learning_briefs_LearningBriefId",
                        column: x => x.LearningBriefId,
                        principalTable: "learning_briefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_learning_brief_stories_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_learning_brief_stories_LearningBriefId_StoryId",
                table: "learning_brief_stories",
                columns: new[] { "LearningBriefId", "StoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_learning_brief_stories_StoryId",
                table: "learning_brief_stories",
                column: "StoryId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_briefs_LearningSuggestionId",
                table: "learning_briefs",
                column: "LearningSuggestionId");

            migrationBuilder.CreateIndex(
                name: "IX_learning_briefs_UserId_CreatedAt",
                table: "learning_briefs",
                columns: new[] { "UserId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "learning_brief_stories");

            migrationBuilder.DropTable(
                name: "learning_briefs");
        }
    }
}
