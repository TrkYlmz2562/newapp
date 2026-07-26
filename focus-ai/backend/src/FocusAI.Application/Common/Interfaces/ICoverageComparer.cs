using FocusAI.Domain.Enums;

namespace FocusAI.Application.Common.Interfaces;

/// <summary>One raw observation, before its quote has been checked.</summary>
public sealed record CoveragePointResult(
    string Text,
    ComparisonKind Kind,
    IReadOnlyList<int> Sources,
    string Quote,
    int QuoteSource);

public sealed record CoverageComparisonResult
{
    public IReadOnlyList<CoveragePointResult> Points { get; init; } = [];

    public string Provider { get; init; } = "none";

    public string Model { get; init; } = "none";

    public bool Succeeded { get; init; }

    public static readonly CoverageComparisonResult None = new();
}

/// <summary>
/// Compares how the outlets covering one story reported it. Runs only when a story
/// has more than one source, because with one source there is nothing to compare.
/// </summary>
public interface ICoverageComparer
{
    int Version { get; }

    Task<CoverageComparisonResult> CompareAsync(
        string title,
        IReadOnlyList<CoverageSource> sources,
        CancellationToken cancellationToken = default);
}

/// <summary>One outlet's own account of the story, as it published it.</summary>
public sealed record CoverageSource(string SourceName, string ArticleTitle, string Text);
