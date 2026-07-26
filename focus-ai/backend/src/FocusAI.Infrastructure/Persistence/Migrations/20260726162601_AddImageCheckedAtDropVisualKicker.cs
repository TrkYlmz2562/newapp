using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddImageCheckedAtDropVisualKicker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VisualKicker",
                table: "story_summaries");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ImageCheckedAt",
                table: "articles",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageCheckedAt",
                table: "articles");

            migrationBuilder.AddColumn<string>(
                name: "VisualKicker",
                table: "story_summaries",
                type: "character varying(48)",
                maxLength: 48,
                nullable: true);
        }
    }
}
