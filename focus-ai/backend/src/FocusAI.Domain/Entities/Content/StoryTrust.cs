using FocusAI.Domain.Common;

namespace FocusAI.Domain.Entities.Content;

/// <summary>
/// Stored breakdown behind the 0-100 trust score (PRD section 5.4). Persisting
/// the components — not just the total — lets the UI explain *why* a story
/// scored what it did, which is the whole point of showing a number.
/// </summary>
public class StoryTrust : AuditableEntity
{
    public Guid StoryId { get; set; }

    public Story? Story { get; set; }

    /// <summary>"Resmi kaynak" — 0-100.</summary>
    public int OfficialSourceScore { get; set; }

    /// <summary>"Kaç kaynak doğruladı" — 0-100.</summary>
    public int CorroborationScore { get; set; }

    /// <summary>"Tarih" — freshness decay, 0-100.</summary>
    public int RecencyScore { get; set; }

    /// <summary>"Teknik doğruluk" — LLM-judged claim specificity, 0-100.</summary>
    public int TechnicalAccuracyScore { get; set; }

    /// <summary>"Topluluk güveni" — engagement relative to the source's baseline, 0-100.</summary>
    public int CommunityScore { get; set; }

    /// <summary>Weighted composite actually shown to the user.</summary>
    public int Total { get; set; }

    /// <summary>Short human-readable justification, e.g. "3 resmi kaynak doğruladı".</summary>
    public string? Explanation { get; set; }

    public DateTimeOffset CalculatedAt { get; set; }
}
