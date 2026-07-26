using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Scoring;

public sealed record DigestCandidate(
    Guid StoryId,
    double Score,
    ContentCategory Category,
    int ReadingMinutes,
    string Reason);

/// <summary>
/// Turns a ranked candidate list into the actual daily edition.
/// </summary>
/// <remarks>
/// Pure ranking produces a monoculture: on a big model-release day the top ten
/// would all be the same story from ten angles, which defeats the promise of
/// "Günün Bilmen Gereken 10 Konusu". This applies a per-category cap first, then
/// backfills from the leftovers so the edition is always full.
/// </remarks>
public static class DigestComposer
{
    public const int DefaultTake = 10;

    /// <summary>No single category may occupy more than this many slots on the first pass.</summary>
    public const int DefaultMaxPerCategory = 4;

    public static IReadOnlyList<DigestCandidate> Compose(
        IEnumerable<DigestCandidate> ranked,
        int take = DefaultTake,
        int maxPerCategory = DefaultMaxPerCategory)
    {
        take = Math.Max(1, take);
        maxPerCategory = Math.Max(1, maxPerCategory);

        var ordered = ranked
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.StoryId)
            .ToList();

        var selected = new List<DigestCandidate>(take);
        var perCategory = new Dictionary<ContentCategory, int>();
        var overflow = new List<DigestCandidate>();

        foreach (var candidate in ordered)
        {
            if (selected.Count == take)
            {
                break;
            }

            perCategory.TryGetValue(candidate.Category, out var used);
            if (used >= maxPerCategory)
            {
                overflow.Add(candidate);
                continue;
            }

            perCategory[candidate.Category] = used + 1;
            selected.Add(candidate);
        }

        // A slow day in every category but one is still better filled than short.
        foreach (var candidate in overflow)
        {
            if (selected.Count == take)
            {
                break;
            }

            selected.Add(candidate);
        }

        return selected;
    }

    /// <summary>
    /// Reading time for the composed edition. The PRD promises roughly five
    /// minutes, so this is what the generator checks itself against.
    /// </summary>
    public static int EstimateReadingMinutes(IEnumerable<DigestCandidate> selected) =>
        Math.Max(1, selected.Sum(c => Math.Max(1, c.ReadingMinutes)));
}
