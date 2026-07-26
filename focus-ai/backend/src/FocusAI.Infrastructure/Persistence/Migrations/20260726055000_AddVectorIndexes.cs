using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FocusAI.Infrastructure.Persistence.Migrations;

/// <summary>
/// Approximate-nearest-neighbour indexes for the embedding columns.
/// </summary>
/// <remarks>
/// Written by hand because EF Core has no model-level concept of an HNSW index.
/// Without these, every related-story lookup and every RAG retrieval is a
/// sequential scan over the whole table — fine at a thousand rows, unusable at a
/// million. <c>vector_cosine_ops</c> matches the <c>&lt;=&gt;</c> operator used
/// by <see cref="PgVectorSearch"/>; a different operator class would leave the
/// index unused.
/// </remarks>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260726055000_AddVectorIndexes")]
public partial class AddVectorIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // m/ef_construction are the pgvector defaults: good recall at a build
        // cost that stays reasonable for a table this size.
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_stories_Embedding_hnsw"
            ON stories USING hnsw ("Embedding" vector_cosine_ops)
            WITH (m = 16, ef_construction = 64);
            """);

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_articles_Embedding_hnsw"
            ON articles USING hnsw ("Embedding" vector_cosine_ops)
            WITH (m = 16, ef_construction = 64);
            """);

        // De-duplication scans SimHash by Hamming distance within a time window,
        // so the window predicate needs to be indexed too.
        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_articles_SimHash_PublishedAt"
            ON articles ("SimHash", "PublishedAt");
            """);

        // Trigram index behind the ILIKE fallback used when Meilisearch is absent.
        migrationBuilder.Sql("""CREATE EXTENSION IF NOT EXISTS pg_trgm;""");

        migrationBuilder.Sql("""
            CREATE INDEX IF NOT EXISTS "IX_stories_Title_trgm"
            ON stories USING gin (lower("Title") gin_trgm_ops);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_stories_Title_trgm";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_articles_SimHash_PublishedAt";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_articles_Embedding_hnsw";""");
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_stories_Embedding_hnsw";""");
    }
}
