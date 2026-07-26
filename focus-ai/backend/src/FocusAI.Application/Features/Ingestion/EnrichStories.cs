using FocusAI.Application.Common.Interfaces;
using FocusAI.Application.Dtos;
using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Scoring;
using FocusAI.Domain.Text;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FocusAI.Application.Features.Ingestion;

/// <summary>
/// Stages 7-9 of the PRD section 13 pipeline: LLM Summary → Importance Score →
/// publish. Turns a raw cluster into something worth putting in front of a reader.
/// </summary>
public sealed record EnrichStoriesCommand(int BatchSize = 25, Guid? StoryId = null)
    : IRequest<IngestionReportDto>;

public sealed class EnrichStoriesCommandHandler(
    IApplicationDbContext db,
    IContentAiService ai,
    ISearchIndex searchIndex,
    IDateTimeProvider clock,
    ILogger<EnrichStoriesCommandHandler> logger) : IRequestHandler<EnrichStoriesCommand, IngestionReportDto>
{
    /// <summary>Excerpt budget per member article handed to the model.</summary>
    private const int ExcerptLength = 2500;

    /// <summary>At most this many member articles are quoted into the prompt.</summary>
    private const int MaxArticlesInPrompt = 6;

    public async Task<IngestionReportDto> Handle(
        EnrichStoriesCommand request,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;

        var query = db.Stories
            .Include(s => s.Articles).ThenInclude(a => a.Source)
            .Include(s => s.Summary)
            .Include(s => s.Analysis)
            .Include(s => s.Trust)
            .Include(s => s.Topics)
            .AsQueryable();

        query = request.StoryId is { } id
            ? query.Where(s => s.Id == id)
            : query
                .Where(s => s.Status == StoryStatus.Draft || s.Status == StoryStatus.Enriching)
                .OrderByDescending(s => s.LastActivityAt)
                .Take(Math.Clamp(request.BatchSize, 1, 200));

        var stories = await query.ToListAsync(cancellationToken);
        if (stories.Count == 0)
        {
            return new IngestionReportDto(0, 0, 0, 0, 0, 0, []);
        }

        var topicTerms = await LoadTopicTermsAsync(cancellationToken);
        var topicsBySlug = await db.Topics.ToDictionaryAsync(t => t.Slug, cancellationToken);

        var errors = new List<string>();
        var published = 0;
        var failures = 0;

        foreach (var story in stories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                story.Status = StoryStatus.Enriching;

                var context = BuildPromptContext(story);
                var summary = await ai.SummarizeAsync(context, cancellationToken);
                var analysis = await ai.AnalyzeAsync(context, cancellationToken);

                ApplySummary(story, summary, now);
                ApplyAnalysis(story, analysis, now);
                ApplyTopics(story, summary.TopicSlugs, topicsBySlug, topicTerms);
                ApplyScores(story, summary, now);

                story.Status = StoryStatus.Published;
                story.UpdatedAt = now;
                published++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Leave the story in Enriching so the next run retries it rather
                // than publishing a half-enriched card.
                failures++;
                errors.Add($"{story.Slug}: {ex.Message}");
                logger.LogError(ex, "FocusAI enrichment failed for story {StorySlug}", story.Slug);
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var readyToIndex = stories.Where(s => s.Status == StoryStatus.Published).ToList();
        if (readyToIndex.Count > 0 && searchIndex.IsEnabled)
        {
            try
            {
                await searchIndex.IndexAsync(readyToIndex, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Search is an accelerator, not a dependency — the DB fallback covers it.
                logger.LogWarning(ex, "FocusAI search indexing failed for {Count} stories", readyToIndex.Count);
            }
        }

        logger.LogInformation("FocusAI enriched and published {Published} stories", published);

        return new IngestionReportDto(0, 0, 0, 0, published, failures, errors);
    }

    private static StoryPromptContext BuildPromptContext(Story story)
    {
        var articles = story.Articles
            .OrderByDescending(a => a.IsPrimary)
            .ThenByDescending(a => a.Source?.IsOfficial ?? false)
            .ThenBy(a => a.PublishedAt)
            .Take(MaxArticlesInPrompt)
            .ToList();

        return new StoryPromptContext
        {
            Title = story.Title,
            PublishedAt = story.PublishedAt,
            ArticleExcerpts = articles
                .Select(a =>
                {
                    var body = a.BestText();
                    return body.Length <= ExcerptLength ? body : body[..ExcerptLength];
                })
                .ToList(),
            SourceNames = articles
                .Select(a => a.Source?.Name ?? "Bilinmeyen kaynak")
                .Distinct()
                .ToList()
        };
    }

    /// <summary>
    /// The source text a displayed term has to be found in. Deliberately the
    /// articles' own words rather than the model's summary — grounding generated
    /// text against other generated text proves nothing.
    /// </summary>
    private static string SourceCorpus(Story story) =>
        string.Join(' ', story.Articles.Select(a => $"{a.Title} {a.BestText()}"));

    private void ApplySummary(Story story, StorySummaryResult result, DateTimeOffset now)
    {
        if (story.Summary is null)
        {
            story.Summary = new StorySummary { StoryId = story.Id, Summary = result.Summary, CreatedAt = now };

            // Explicit Add is required, not stylistic. BaseEntity assigns the key
            // in its initializer, so a child discovered through a tracked
            // parent's navigation already has a non-default key and EF tracks it
            // as Modified — issuing an UPDATE against a row that does not exist.
            db.StorySummaries.Add(story.Summary);
        }

        // Every field below is model output and is capped at its column width.
        story.Summary.Summary = FieldLimits.Cap(result.Summary, FieldLimits.Summary)!;
        story.Summary.WhyItMatters = FieldLimits.Cap(result.WhyItMatters, FieldLimits.SummarySection);
        story.Summary.WhoIsAffected = FieldLimits.Cap(result.WhoIsAffected, FieldLimits.SummarySection);
        story.Summary.WhatShouldIDo = FieldLimits.Cap(result.WhatShouldIDo, FieldLimits.SummarySection);
        story.Summary.KeyPoints = result.KeyPoints
            .Take(FieldLimits.KeyPointCount)
            .Select(point => FieldLimits.Cap(point, FieldLimits.KeyPoint)!)
            .Where(point => !string.IsNullOrWhiteSpace(point))
            .ToList();
        // The subject is validated rather than capped: it is set in display type,
        // so a truncated term ("OpenSS…") reads as broken. Sanitize re-derives from
        // the headline whenever the model's answer is missing, over-long, merely
        // the category restated, or — the case a prompt cannot prevent — absent
        // from the story altogether.
        story.Summary.VisualEntity = VisualSubject.Sanitize(
            result.VisualEntity,
            result.Title ?? story.Title,
            SourceCorpus(story),
            FieldLimits.VisualEntity);
        story.Summary.Provider = FieldLimits.Cap(result.Provider, FieldLimits.ProviderName);
        story.Summary.Model = FieldLimits.Cap(result.Model, FieldLimits.ModelName);
        story.Summary.PromptTokens = result.PromptTokens;
        story.Summary.CompletionTokens = result.CompletionTokens;
        story.Summary.GeneratedAt = now;
        story.Summary.UpdatedAt = now;

        if (!string.IsNullOrWhiteSpace(result.Title))
        {
            story.Title = FieldLimits.Cap(result.Title, FieldLimits.StoryTitle)!;

            // The slug is a permalink: only mint one while the story is still a
            // draft, so links shared from a published card never break.
            if (story.Status != StoryStatus.Published && string.IsNullOrWhiteSpace(story.Slug))
            {
                story.Slug = Slugger.SlugifyUnique(result.Title, story.Id);
            }
        }

        story.Dek = FieldLimits.Cap(result.Dek, FieldLimits.StoryDek) ?? story.Dek;
        story.ReadingMinutes = Math.Max(1, result.ReadingMinutes);

        if (result.Category != ContentCategory.Unknown)
        {
            story.Category = result.Category;
        }
    }

    private void ApplyAnalysis(Story story, StoryAnalysisResult result, DateTimeOffset now)
    {
        if (story.Analysis is null)
        {
            story.Analysis = new StoryAnalysis
            {
                StoryId = story.Id,
                WhyImportant = result.WhyImportant,
                CreatedAt = now
            };

            db.StoryAnalyses.Add(story.Analysis);
        }

        story.Analysis.WhyImportant = FieldLimits.Cap(result.WhyImportant, FieldLimits.AnalysisSection)!;
        story.Analysis.RealImpact = FieldLimits.Cap(result.RealImpact, FieldLimits.AnalysisSection);
        story.Analysis.Hype = result.Hype;
        story.Analysis.HypeReasoning = FieldLimits.Cap(result.HypeReasoning, FieldLimits.AnalysisSection);
        story.Analysis.LearnUrgency = result.LearnUrgency;
        story.Analysis.Longevity = result.Longevity;
        story.Analysis.LongevityReasoning =
            FieldLimits.Cap(result.LongevityReasoning, FieldLimits.AnalysisSection);
        story.Analysis.StackNotes = result.StackNotes.ToDictionary(
            note => note.Key,
            note => FieldLimits.Cap(note.Value, FieldLimits.StackNote)!);
        story.Analysis.Confidence = result.Confidence;
        story.Analysis.Provider = FieldLimits.Cap(result.Provider, FieldLimits.ProviderName);
        story.Analysis.Model = FieldLimits.Cap(result.Model, FieldLimits.ModelName);
        story.Analysis.GeneratedAt = now;
        story.Analysis.UpdatedAt = now;
    }

    /// <summary>
    /// Union of the model's suggested slugs and the deterministic alias match, so
    /// tagging still works with no LLM configured.
    /// </summary>
    private void ApplyTopics(
        Story story,
        IReadOnlyList<string> suggestedSlugs,
        IReadOnlyDictionary<string, Topic> topicsBySlug,
        IReadOnlyCollection<TopicTerm> topicTerms)
    {
        var resolved = new Dictionary<Guid, double>();

        foreach (var slug in suggestedSlugs.Select(s => s.Trim().ToLowerInvariant()).Distinct())
        {
            if (topicsBySlug.TryGetValue(slug, out var topic))
            {
                resolved[topic.Id] = 1.0;
            }
        }

        var body = string.Join(' ', story.Articles.Take(3).Select(a => a.BestText()));
        foreach (var match in TopicMatcher.Match(story.Title, body, topicTerms))
        {
            resolved[match.TopicId] = Math.Max(
                resolved.TryGetValue(match.TopicId, out var existing) ? existing : 0d,
                match.Weight);
        }

        var current = story.Topics.ToDictionary(t => t.TopicId);

        foreach (var (topicId, weight) in resolved)
        {
            if (current.TryGetValue(topicId, out var link))
            {
                link.Weight = weight;
            }
            else
            {
                var added = new StoryTopic { StoryId = story.Id, TopicId = topicId, Weight = weight };
                story.Topics.Add(added);
                db.StoryTopics.Add(added);
            }
        }

        foreach (var (topicId, link) in current.Where(kv => !resolved.ContainsKey(kv.Key)))
        {
            story.Topics.Remove(link);
            db.StoryTopics.Remove(link);
        }
    }

    private void ApplyScores(Story story, StorySummaryResult summary, DateTimeOffset now)
    {
        var sources = story.Articles
            .Where(a => a.Source is not null)
            .Select(a => a.Source!)
            .DistinctBy(s => s.Id)
            .ToList();

        var trustInput = new TrustScoreInput
        {
            DistinctSourceCount = Math.Max(1, story.SourceCount),
            OfficialSourceCount = story.OfficialSourceCount,
            MaxSourceTrustWeight = sources.Count > 0 ? sources.Max(s => s.TrustWeight) : 0.5,
            PublishedAt = story.PublishedAt,
            Now = now,
            EngagementScore = story.EngagementScore,
            TechnicalAccuracy = summary.TechnicalAccuracy
        };

        var trust = TrustScoreCalculator.Calculate(trustInput);

        if (story.Trust is null)
        {
            story.Trust = new StoryTrust { StoryId = story.Id, CreatedAt = now };
            db.StoryTrusts.Add(story.Trust);
        }

        story.Trust.OfficialSourceScore = trust.OfficialSourceScore;
        story.Trust.CorroborationScore = trust.CorroborationScore;
        story.Trust.RecencyScore = trust.RecencyScore;
        story.Trust.TechnicalAccuracyScore = trust.TechnicalAccuracyScore;
        story.Trust.CommunityScore = trust.CommunityScore;
        story.Trust.Total = trust.Total;
        story.Trust.Explanation = trust.Explanation;
        story.Trust.CalculatedAt = now;
        story.Trust.UpdatedAt = now;

        story.TrustScore = trust.Total;

        story.ImportanceScore = ImportanceScoreCalculator.Calculate(new ImportanceScoreInput
        {
            TrustScore = trust.Total,
            DistinctSourceCount = Math.Max(1, story.SourceCount),
            OfficialSourceCount = story.OfficialSourceCount,
            EngagementScore = story.EngagementScore,
            PublishedAt = story.PublishedAt,
            Now = now,
            LlmImportance = summary.Importance,
            Category = story.Category
        });
    }

    private async Task<IReadOnlyList<TopicTerm>> LoadTopicTermsAsync(CancellationToken cancellationToken)
    {
        var topics = await db.Topics
            .AsNoTracking()
            .Select(t => new { t.Id, t.Slug, t.Name, t.Aliases })
            .ToListAsync(cancellationToken);

        return topics
            .Select(t => new TopicTerm(
                t.Id,
                t.Slug,
                new[] { t.Name }.Concat(t.Aliases).Distinct().ToList()))
            .ToList();
    }
}
