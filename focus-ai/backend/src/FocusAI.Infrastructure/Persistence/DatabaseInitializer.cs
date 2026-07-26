using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Persistence;

/// <summary>
/// Applies migrations and reconciles the seed catalogue at startup.
/// </summary>
/// <remarks>
/// Seeding is upsert-by-slug rather than insert-if-empty: adding a source to
/// <see cref="SeedData"/> ships it to existing deployments on the next restart,
/// while never overwriting operator edits to enablement or fetch cadence.
/// </remarks>
public sealed class DatabaseInitializer(
    ApplicationDbContext db,
    ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (db.Database.IsRelational())
            {
                await db.Database.MigrateAsync(cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "FocusAI database migration failed");
            throw;
        }

        await SeedTopicsAsync(cancellationToken);
        await SeedSourcesAsync(cancellationToken);
        await SeedBadgesAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedTopicsAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Topics.ToDictionaryAsync(t => t.Slug, cancellationToken);
        var added = 0;

        foreach (var topic in SeedData.Topics())
        {
            if (existing.TryGetValue(topic.Slug, out var current))
            {
                // Aliases are the tagger's vocabulary; keeping them current is the
                // whole point of re-running the seed.
                current.Name = topic.Name;
                current.Kind = topic.Kind;
                current.Category = topic.Category;
                current.Aliases = topic.Aliases;
                continue;
            }

            db.Topics.Add(topic);
            added++;
        }

        if (added > 0)
        {
            logger.LogInformation("FocusAI seeded {Count} new topics", added);
        }
    }

    private async Task SeedSourcesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Sources.ToDictionaryAsync(s => s.Slug, cancellationToken);
        var added = 0;

        foreach (var source in SeedData.Sources())
        {
            if (existing.TryGetValue(source.Slug, out var current))
            {
                // Feed URLs move; trust weights get retuned. Neither is operator state.
                current.Name = source.Name;
                current.FeedUrl = source.FeedUrl;
                current.WebsiteUrl = source.WebsiteUrl;
                current.Kind = source.Kind;
                current.Category = source.Category;
                current.DefaultContentCategory = source.DefaultContentCategory;
                current.IsOfficial = source.IsOfficial;
                current.TrustWeight = source.TrustWeight;
                // Also not operator state, and now load-bearing: the language is
                // what decides whether a story's extracted text gets translated.
                current.Language = source.Language;
                continue;
            }

            db.Sources.Add(source);
            added++;
        }

        if (added > 0)
        {
            logger.LogInformation("FocusAI seeded {Count} new sources", added);
        }
    }

    private async Task SeedBadgesAsync(CancellationToken cancellationToken)
    {
        var existing = await db.Badges.Select(b => b.Slug).ToListAsync(cancellationToken);
        var known = existing.ToHashSet();

        foreach (var badge in SeedData.Badges().Where(b => !known.Contains(b.Slug)))
        {
            db.Badges.Add(badge);
        }
    }
}
