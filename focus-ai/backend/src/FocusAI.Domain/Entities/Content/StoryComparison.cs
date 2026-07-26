using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// Where the outlets covering one story agree, and where they part company.
/// </summary>
/// <remarks>
/// This is the thing an aggregator can say that no single outlet can. It is also
/// the easiest place to put words in someone's mouth, so every point carries the
/// sentence it came from and the outlet that wrote it, and a point whose quote
/// cannot be found in that outlet's own text is discarded before storage.
/// </remarks>
public class StoryComparison : AuditableEntity
{
    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public List<ComparisonPoint> Points { get; set; } = [];

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public int ClassifierVersion { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}

/// <summary>
/// One observation about the coverage. <see cref="Quote"/> is a verbatim sentence
/// from <see cref="QuoteSource"/> — without it the point is not stored.
/// </summary>
public sealed record ComparisonPoint(
    string Text,
    ComparisonKind Kind,
    IReadOnlyList<string> Sources,
    string Quote,
    string QuoteSource);
