using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusAI.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddStoryEvidentialClaim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "HasEvidentialClaim",
                table: "stories",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Best-effort backfill for finance stories already published. Without it
            // every existing row reads as "no hearsay detected" and can reach the
            // feed on corroboration alone — and published stories are never
            // re-enriched, so nothing would ever correct them.
            //
            // Approximates CommitmentLexicon.HasEvidentialSuffix, which stays the
            // authority: words ending in the -mış evidential. \m and \M are Postgres
            // word boundaries, so "yapılmıştır" does not match — it ends in -tır,
            // the assertive form, which is the opposite of what this looks for.
            // Only Title and Dek are checked; the summary lives in another table and
            // this is a one-time approximation, not the rule.
            migrationBuilder.Sql("""
                UPDATE stories
                SET "HasEvidentialClaim" = true
                WHERE "Category" = 11
                  AND (
                    COALESCE("Title", '') ~* '\m\w*(mış|miş|muş|müş)\M'
                    OR COALESCE("Dek", '') ~* '\m\w*(mış|miş|muş|müş)\M'
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasEvidentialClaim",
                table: "stories");
        }
    }
}
