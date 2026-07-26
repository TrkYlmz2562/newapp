using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFinanceCommitment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "story_commitments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tier = table.Column<int>(type: "integer", nullable: false),
                    ModelTier = table.Column<int>(type: "integer", nullable: false),
                    Horizon = table.Column<int>(type: "integer", nullable: false),
                    ClaimSource = table.Column<int>(type: "integer", nullable: false),
                    Instrument = table.Column<int>(type: "integer", nullable: false),
                    Event = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DateText = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: true),
                    DatePrecision = table.Column<int>(type: "integer", nullable: false),
                    Quote = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    Condition = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    Reference = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    IsReversed = table.Column<bool>(type: "boolean", nullable: false),
                    ClassifierVersion = table.Column<int>(type: "integer", nullable: false),
                    ClassifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_story_commitments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_story_commitments_stories_StoryId",
                        column: x => x.StoryId,
                        principalTable: "stories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_story_commitments_StoryId",
                table: "story_commitments",
                column: "StoryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_story_commitments_Tier_Horizon_EventDate",
                table: "story_commitments",
                columns: new[] { "Tier", "Horizon", "EventDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "story_commitments");
        }
    }
}
