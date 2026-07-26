using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Trends;

/// <summary>
/// Per-period mention counts behind the "Aylık Trend" charts (PRD section 5.9).
/// One row per topic per period start.
/// </summary>
public class TrendSnapshot : BaseEntity
{
    public Guid TopicId { get; set; }

    public Topic? Topic { get; set; }

    public DigestPeriod Period { get; set; } = DigestPeriod.Monthly;

    /// <summary>First day of the bucket (week start for weekly, month start for monthly).</summary>
    public DateOnly PeriodStart { get; set; }

    public int StoryCount { get; set; }

    public int SourceCount { get; set; }

    /// <summary>Sum of member story importance — separates loud from significant.</summary>
    public int WeightedImportance { get; set; }

    /// <summary>Percentage change in <see cref="StoryCount"/> against the previous period.</summary>
    public double MomentumPercent { get; set; }

    public DateTimeOffset CalculatedAt { get; set; }
}
