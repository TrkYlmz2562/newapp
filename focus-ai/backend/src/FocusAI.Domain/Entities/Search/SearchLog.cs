using FocusAI.Domain.Common;

namespace FocusAI.Domain.Entities.Search;

/// <summary>
/// Natural-language queries (PRD section 9) plus what the parser made of them.
/// Doubles as the evaluation set for improving the query parser.
/// </summary>
public class SearchLog : BaseEntity
{
    public Guid? UserId { get; set; }

    public required string RawQuery { get; set; }

    /// <summary>Serialised structured filter the LLM produced from the raw query.</summary>
    public string? ParsedFilterJson { get; set; }

    public int ResultCount { get; set; }

    public int LatencyMs { get; set; }

    public bool UsedLlmParsing { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
