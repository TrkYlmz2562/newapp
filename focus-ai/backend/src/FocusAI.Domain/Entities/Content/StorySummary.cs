using FocusAI.Domain.Common;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// The five-part AI summary from PRD section 5.2. One row per story per
/// language, so a Turkish and an English reader each get native copy.
/// </summary>
public class StorySummary : AuditableEntity
{
    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    public string Language { get; set; } = "tr";

    /// <summary>"Kısa özet" — 2-4 sentences, no marketing language.</summary>
    public required string Summary { get; set; }

    /// <summary>"Neden önemli?"</summary>
    public string? WhyItMatters { get; set; }

    /// <summary>"Kimleri etkiliyor?"</summary>
    public string? WhoIsAffected { get; set; }

    /// <summary>"Ben ne yapmalıyım?" — concrete next action, or an explicit "nothing".</summary>
    public string? WhatShouldIDo { get; set; }

    /// <summary>Three to five scannable bullets for the card back.</summary>
    public List<string> KeyPoints { get; set; } = [];

    /// <summary>Longer body unlocked for Premium (PRD section 16).</summary>
    public string? ExtendedSummary { get; set; }

    /// <summary>
    /// The subject the card sets in display type — "M5", "OpenSSH", "20M$".
    /// Always a literal term from the story, never a coined phrase.
    /// </summary>
    public string? VisualEntity { get; set; }

    /// <summary>One-line descriptor shown under the subject ("Muhakeme modeli").</summary>
    public string? VisualKicker { get; set; }

    public string? Provider { get; set; }

    public string? Model { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    public DateTimeOffset GeneratedAt { get; set; }
}
