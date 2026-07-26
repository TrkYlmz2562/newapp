using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Text;

namespace FocusAI.Application.Features.Ingestion;

/// <summary>
/// Keeps only the coverage observations that can be traced back to the outlet
/// they are attributed to.
/// </summary>
/// <remarks>
/// Putting words in a named outlet's mouth is worse than saying nothing, so the
/// check here is per-source rather than per-story: a point's quote must appear in
/// the text of the exact article it cites, not merely somewhere in the cluster.
/// Points that fail are dropped individually — each one stands alone, so losing a
/// bad one does not taint the rest.
/// </remarks>
public static class CoverageEvaluator
{
    /// <summary>A handful of well-chosen lines; past that the section stops being read.</summary>
    private const int MaxPoints = 6;

    public static StoryComparison? Evaluate(
        CoverageComparisonResult result,
        IReadOnlyList<CoverageSource> sources,
        DateTimeOffset now,
        int classifierVersion)
    {
        if (!result.Succeeded || sources.Count < 2)
        {
            return null;
        }

        var verified = new List<ComparisonPoint>();

        foreach (var point in result.Points)
        {
            if (point.QuoteSource < 0 || point.QuoteSource >= sources.Count)
            {
                continue;
            }

            var attributed = sources[point.QuoteSource];

            // The whole claim of this section is "outlet X said this" — so the
            // sentence has to be in outlet X's own article.
            if (!OccursIn(point.Quote, $"{attributed.ArticleTitle} {attributed.Text}"))
            {
                continue;
            }

            var named = point.Sources
                .Where(index => index >= 0 && index < sources.Count)
                .Select(index => sources[index].SourceName)
                .Distinct()
                .ToList();

            if (named.Count == 0)
            {
                named = [attributed.SourceName];
            }

            // A claim that only one outlet reports is Unique by definition,
            // whatever the model labelled it.
            var kind = named.Count == 1 && point.Kind == ComparisonKind.Shared
                ? ComparisonKind.Unique
                : point.Kind;

            verified.Add(new ComparisonPoint(
                FieldLimits.Cap(point.Text, FieldLimits.ComparisonPoint)!,
                kind,
                named,
                FieldLimits.Cap(point.Quote, FieldLimits.CommitmentQuote)!,
                attributed.SourceName));

            if (verified.Count == MaxPoints)
            {
                break;
            }
        }

        if (verified.Count == 0)
        {
            return null;
        }

        return new StoryComparison
        {
            Points = verified,
            Provider = FieldLimits.Cap(result.Provider, FieldLimits.ProviderName),
            Model = FieldLimits.Cap(result.Model, FieldLimits.ModelName),
            ClassifierVersion = classifierVersion,
            GeneratedAt = now,
            CreatedAt = now
        };
    }

    private static bool OccursIn(string span, string text)
    {
        var needle = CommitmentLexicon.Fold(span);
        return needle.Length > 0 && CommitmentLexicon.Fold(text).Contains(needle, StringComparison.Ordinal);
    }
}
