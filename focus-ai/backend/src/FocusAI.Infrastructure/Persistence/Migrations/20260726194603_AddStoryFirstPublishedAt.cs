using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryFirstPublishedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FirstPublishedAt",
                table: "stories",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill is not optional here. The column decides whether a story's
            // permalink is frozen and whether its detail page is servable, so
            // leaving every existing row null would 404 the whole archive and let
            // the next enrichment re-mint slugs that people already have links to.
            //
            // A story counts as previously published if it says so (Status = 2), or
            // if it ever finished enrichment — which is what a story_summaries row
            // means, and which catches the stories that clustering has since demoted
            // back to Enriching.
            //
            // The timestamp is the best evidence available: when its summary was
            // first written, else when the row was created. Neither is the true
            // first-publication moment, but both are before now, which is all the
            // column is asked to establish.
            migrationBuilder.Sql("""
                UPDATE stories s
                SET "FirstPublishedAt" = COALESCE(
                    (SELECT MIN(sum."CreatedAt") FROM story_summaries sum WHERE sum."StoryId" = s."Id"),
                    s."CreatedAt")
                WHERE s."FirstPublishedAt" IS NULL
                  AND (
                    s."Status" = 2
                    OR EXISTS (SELECT 1 FROM story_summaries sum WHERE sum."StoryId" = s."Id")
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FirstPublishedAt",
                table: "stories");
        }
    }
}
