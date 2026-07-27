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
        await RepairStorySlugsAsync(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Lowercases story slugs minted before the normaliser learned about 'İ'.
    /// </summary>
    /// <remarks>
    /// Those slugs carry a bare capital 'I' where a dotted capital I stood in the
    /// headline, and the detail and speech lookups both lowercase the slug they are
    /// handed before matching — so an affected story returned 404 for its own URL,
    /// from every link that ever pointed at it.
    ///
    /// The stored value is lowercased rather than re-slugged from the title, and
    /// that is the point: a reader's existing link lowercases to exactly this, so
    /// the links already in the wild start resolving instead of being replaced by
    /// new ones. Re-slugging would fix the story and break the link.
    ///
    /// Idempotent, and a no-op on every start after the first.
    /// </remarks>
    private async Task RepairStorySlugsAsync(CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            return;
        }

        var broken = await db.Stories
            .Where(s => s.Slug != s.Slug.ToLower())
            .ToListAsync(cancellationToken);

        if (broken.Count == 0)
        {
            return;
        }

        // Slug is unique-indexed. A lowered value that already belongs to another
        // story has to be left alone rather than take the index down on startup.
        var brokenIds = broken.Select(s => s.Id).ToList();
        var taken = (await db.Stories
                .Where(s => !brokenIds.Contains(s.Id))
                .Select(s => s.Slug)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var repaired = 0;

        foreach (var story in broken)
        {
            var lowered = story.Slug.ToLowerInvariant();

            if (!taken.Add(lowered))
            {
                logger.LogWarning(
                    "FocusAI left story slug {Slug} as it is: {Lowered} is already taken", story.Slug, lowered);
                continue;
            }

            story.Slug = lowered;
            repaired++;
        }

        if (repaired > 0)
        {
            logger.LogInformation("FocusAI lowercased {Count} unreachable story slugs", repaired);
        }
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
