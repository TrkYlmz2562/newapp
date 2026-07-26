using System.Linq.Expressions;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Entities.Content;

namespace FocusAI.Application.Common.Mappings;

/// <summary>
/// Expression-tree projections so list queries stay a single translatable SELECT
/// instead of loading whole aggregates and mapping in memory.
/// </summary>
public static class StoryProjections
{
    public static Expression<Func<Story, StoryCardDto>> ToCard(string language = "tr") =>
        story => new StoryCardDto
        {
            Id = story.Id,
            Slug = story.Slug,
            Title = story.Title,
            Dek = story.Dek,
            Category = story.Category,
            Summary = story.Summary != null ? story.Summary.Summary : null,
            WhyItMatters = story.Summary != null ? story.Summary.WhyItMatters : null,
            HeroImageUrl = story.HeroImageUrl,
            PublishedAt = story.PublishedAt,
            TrustScore = story.TrustScore,
            ImportanceScore = story.ImportanceScore,
            SourceCount = story.SourceCount,
            ReadingMinutes = story.ReadingMinutes,
            Topics = story.Topics
                .OrderByDescending(t => t.Weight)
                .Select(t => new TopicDto(t.TopicId, t.Topic!.Name, t.Topic.Slug, t.Topic.Kind))
                .ToList()
        };

    /// <summary>
    /// Materialises the full detail aggregate. Related stories and the reader's
    /// bookmark state are attached by the handler, which has the extra context.
    /// </summary>
    public static StoryDetailDto ToDetail(Story story)
    {
        return new StoryDetailDto
        {
            Id = story.Id,
            Slug = story.Slug,
            Title = story.Title,
            Dek = story.Dek,
            Category = story.Category,
            PublishedAt = story.PublishedAt,
            LastActivityAt = story.LastActivityAt,
            HeroImageUrl = story.HeroImageUrl,
            ReadingMinutes = story.ReadingMinutes,
            ImportanceScore = story.ImportanceScore,
            Summary = story.Summary?.Summary,
            WhyItMatters = story.Summary?.WhyItMatters,
            WhoIsAffected = story.Summary?.WhoIsAffected,
            WhatShouldIDo = story.Summary?.WhatShouldIDo,
            KeyPoints = story.Summary?.KeyPoints ?? [],
            ExtendedSummary = story.Summary?.ExtendedSummary,
            Trust = story.Trust is null
                ? null
                : new TrustDto(
                    story.Trust.Total,
                    story.Trust.OfficialSourceScore,
                    story.Trust.CorroborationScore,
                    story.Trust.RecencyScore,
                    story.Trust.TechnicalAccuracyScore,
                    story.Trust.CommunityScore,
                    story.Trust.Explanation),
            Analysis = story.Analysis is null
                ? null
                : new AnalysisDto(
                    story.Analysis.WhyImportant,
                    story.Analysis.RealImpact,
                    story.Analysis.Hype,
                    story.Analysis.HypeReasoning,
                    story.Analysis.LearnUrgency,
                    story.Analysis.Longevity,
                    story.Analysis.LongevityReasoning,
                    story.Analysis.StackNotes,
                    story.Analysis.Confidence),
            Sources = story.Articles
                .OrderByDescending(a => a.IsPrimary)
                .ThenBy(a => a.PublishedAt)
                .Select(a => new SourceRefDto(
                    a.SourceId,
                    a.Source?.Name ?? "Bilinmeyen kaynak",
                    a.Source?.Slug ?? "unknown",
                    a.CanonicalUrl,
                    a.Source?.IsOfficial ?? false,
                    a.Source?.IconUrl,
                    a.PublishedAt,
                    a.Title))
                .ToList(),
            Links = story.Links
                .OrderBy(l => l.Position)
                .Select(l => new StoryLinkDto(l.Kind, l.Url, l.Title, l.Description, l.ThumbnailUrl))
                .ToList(),
            Topics = story.Topics
                .OrderByDescending(t => t.Weight)
                .Select(t => new TopicDto(t.TopicId, t.Topic?.Name ?? t.TopicId.ToString(), t.Topic?.Slug ?? "", t.Topic?.Kind ?? Domain.Enums.TopicKind.Concept))
                .ToList()
        };
    }

    /// <summary>
    /// Picks the stack note that matches one of the reader's interests, so a
    /// .NET developer sees the .NET guidance rather than the generic paragraph.
    /// </summary>
    public static string? PickPersonalNote(
        IReadOnlyDictionary<string, string> stackNotes,
        IReadOnlyCollection<string> interestSlugs)
    {
        if (stackNotes.Count == 0 || interestSlugs.Count == 0)
        {
            return null;
        }

        foreach (var slug in interestSlugs)
        {
            if (stackNotes.TryGetValue(slug, out var note) && !string.IsNullOrWhiteSpace(note))
            {
                return note;
            }
        }

        return null;
    }
}
