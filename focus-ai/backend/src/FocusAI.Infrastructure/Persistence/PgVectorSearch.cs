using FocusAI.Application.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Pgvector;

namespace FocusAI.Infrastructure.Persistence;

/// <summary>
/// Nearest-neighbour lookup over the pgvector column.
/// </summary>
/// <remarks>
/// Written as raw SQL on purpose: the domain keeps embeddings as
/// <c>float[]</c> so it stays provider-agnostic, which means the EF value
/// converter hides the <c>vector</c> type from LINQ and the <c>&lt;=&gt;</c>
/// distance operator cannot be composed in a query. Raw SQL is the honest way to
/// reach it, and it keeps the ordering server-side where it belongs.
/// </remarks>
public sealed class PgVectorSearch(
    ApplicationDbContext db,
    ILogger<PgVectorSearch> logger) : IVectorSearch
{
    public async Task<IReadOnlyList<SearchHit>> FindSimilarStoriesAsync(
        float[] embedding,
        int take,
        Guid? excludeStoryId = null,
        CancellationToken cancellationToken = default)
    {
        if (embedding.Length == 0)
        {
            return [];
        }

        take = Math.Clamp(take, 1, 200);

        // Column names are quoted because EF maps properties PascalCase while the
        // table name itself is snake_case — unquoted identifiers would fold to
        // lowercase and fail to resolve.
        const string sql = """
            SELECT "Id", 1 - ("Embedding" <=> @query) AS score
            FROM stories
            WHERE "Embedding" IS NOT NULL
              AND "Status" = 2
              AND (@exclude IS NULL OR "Id" <> @exclude)
            ORDER BY "Embedding" <=> @query
            LIMIT @take
            """;

        var results = new List<SearchHit>(take);

        try
        {
            var connection = db.Database.GetDbConnection();
            var opened = connection.State != System.Data.ConnectionState.Open;
            if (opened)
            {
                await db.Database.OpenConnectionAsync(cancellationToken);
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.Add(new NpgsqlParameter("query", new Vector(embedding)));
                command.Parameters.Add(new NpgsqlParameter("exclude", (object?)excludeStoryId ?? DBNull.Value));
                command.Parameters.Add(new NpgsqlParameter("take", take));

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    results.Add(new SearchHit(reader.GetGuid(0), reader.GetDouble(1)));
                }
            }
            finally
            {
                if (opened)
                {
                    await db.Database.CloseConnectionAsync();
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Callers all have a non-vector fallback; a missing extension or a
            // not-yet-migrated column must not take the endpoint down.
            logger.LogWarning(ex, "FocusAI vector search unavailable, falling back to non-semantic ranking");
            return [];
        }

        return results;
    }
}
