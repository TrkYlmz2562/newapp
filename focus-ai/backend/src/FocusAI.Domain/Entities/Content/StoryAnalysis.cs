using FocusAI.Domain.Common;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// The opinionated "AI Yorumu" layer from PRD section 5.5 — the part that makes
/// Focus AI an analyst rather than an aggregator.
/// </summary>
public class StoryAnalysis : AuditableEntity
{
    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public string Language { get; set; } = "tr";

    /// <summary>"Bu neden önemli?"</summary>
    public required string WhyImportant { get; set; }

    /// <summary>"Gerçek etkisi nedir?" — separates the announcement from the change.</summary>
    public string? RealImpact { get; set; }

    /// <summary>"Abartılıyor mu?"</summary>
    public HypeLevel Hype { get; set; } = HypeLevel.Accurate;

    public string? HypeReasoning { get; set; }

    /// <summary>"Ne zaman öğrenilmeli?"</summary>
    public LearnUrgency LearnUrgency { get; set; } = LearnUrgency.Watch;

    /// <summary>"Bu teknoloji ölür mü?"</summary>
    public LongevityOutlook Longevity { get; set; } = LongevityOutlook.Uncertain;

    public string? LongevityReasoning { get; set; }

    /// <summary>
    /// Stack-specific guidance, keyed by topic slug — e.g. "dotnet" =&gt; "Eski
    /// projelerini değiştirmen gerekmiyor." Powers the personalised callout on
    /// the detail page (PRD section 5.2 example).
    /// </summary>
    public Dictionary<string, string> StackNotes { get; set; } = [];

    /// <summary>Model self-reported confidence in [0,1]; low values hide the analysis block.</summary>
    public double Confidence { get; set; } = 0.5;

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
