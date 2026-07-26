namespace FocusAI.Domain.Text;

/// <summary>A topic reduced to what matching needs: an id and its search terms.</summary>
public sealed record TopicTerm(Guid TopicId, string Slug, IReadOnlyList<string> Terms);

public sealed record TopicMatch(Guid TopicId, string Slug, double Weight);

/// <summary>
/// Alias-based tagger. The LLM does the interesting classification, but this runs
/// unconditionally so stories are still tagged when no model is configured — and
/// it catches concrete tokens ("dotnet 10", "pgvector") that a summariser paraphrases away.
/// </summary>
public static class TopicMatcher
{
    public static IReadOnlyList<TopicMatch> Match(
        string? title,
        string? body,
        IReadOnlyCollection<TopicTerm> topics)
    {
        if (topics.Count == 0)
        {
            return [];
        }

        var titleTokens = Padded(TextNormalizer.Normalize(title));
        var bodyTokens = Padded(TextNormalizer.Normalize(body));

        var matches = new List<TopicMatch>();

        foreach (var topic in topics)
        {
            double best = 0d;

            foreach (var term in topic.Terms)
            {
                var needle = TextNormalizer.Normalize(term);
                if (needle.Length < 2)
                {
                    continue;
                }

                var padded = $" {needle} ";

                // A term in the headline is a far stronger signal than one buried
                // in the body, so the two positions score differently.
                if (titleTokens.Contains(padded, StringComparison.Ordinal))
                {
                    best = Math.Max(best, 1.0);
                }
                else if (bodyTokens.Contains(padded, StringComparison.Ordinal))
                {
                    best = Math.Max(best, 0.6);
                }
            }

            if (best > 0d)
            {
                matches.Add(new TopicMatch(topic.TopicId, topic.Slug, best));
            }
        }

        return matches
            .OrderByDescending(m => m.Weight)
            .ThenBy(m => m.Slug, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Space-padded so a whole-token search cannot match inside another word —
    /// "go" must not fire on "google".
    /// </summary>
    private static string Padded(string normalized) =>
        normalized.Length == 0 ? string.Empty : $" {normalized} ";
}
