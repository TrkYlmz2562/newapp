using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Ai;

/// <summary>
/// Per-call ledger of LLM spend. Needed both for cost control and for the
/// Premium quota in PRD section 16 ("sınırsız AI soru-cevap" implies the free
/// tier is metered).
/// </summary>
public class AiUsageLog : BaseEntity
{
    public Guid? UserId { get; set; }

    public LlmProviderKind Provider { get; set; }

    public required string Model { get; set; }

    /// <summary>"summary" | "analysis" | "digest-intro" | "search-parse" | "ask" | "learning".</summary>
    public required string Operation { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public int LatencyMs { get; set; }

    public bool Succeeded { get; set; }

    public string? Error { get; set; }

    public DateTimeOffset OccurredAt { get; set; }
}
