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
    IContentTranslator translator,
    ISearchIndex searchIndex,
    IDateTimeProvider clock,
    ICommitmentClassifier commitmentClassifier,
    ICoverageComparer coverageComparer,
    ILogger<EnrichStoriesCommandHandler> logger) : IRequestHandler<EnrichStoriesCommand, IngestionReportDto>
{
    /// <summary>Excerpt budget per member article handed to the model.</summary>
    /// <remarks>
    /// Down from 2500. A news lede front-loads: what happened is in the first few
    /// hundred words and the rest is background the summary was never going to
    /// quote. The excerpts are also sent twice per story — once to summarise, once
    /// to analyse — so every character here is paid for twice.
    /// </remarks>
    private const int ExcerptLength = 1500;

    /// <summary>At most this many member articles are quoted into the prompt.</summary>
    /// <remarks>
    /// Down from 6, and the ordering above is what makes that safe: primary first,
    /// then official, then earliest. The three that survive are the ones carrying
    /// the facts; outlets four through six are usually the same wire copy in
    /// different words, which is precisely what the coverage comparison measures
    /// separately and deterministically.
    /// </remarks>
    private const int MaxArticlesInPrompt = 3;

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
            .Include(s => s.Commitment)
            .Include(s => s.Comparison)
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
                // Status cannot answer "has anyone been able to link to this yet":
                // ClusterArticles demotes a Published story back to Enriching on every
                // new piece of coverage, so a story that has been live for days looks
                // unpublished here. FirstPublishedAt is the durable answer.
                var wasPublished = story.FirstPublishedAt is not null;

                story.Status = StoryStatus.Enriching;

                var context = BuildPromptContext(story);
                // One call for both halves; they were being handed identical
                // excerpts and asked to read them twice.
                var enriched = await ai.EnrichAsync(context, cancellationToken);
                var summary = enriched.Summary;
                var analysis = enriched.Analysis;

                summary = await TranslateAsync(story, context, summary, cancellationToken);

                ApplySummary(story, summary, now, wasPublished);
                ApplyAnalysis(story, analysis, now);
                ApplyTopics(story, summary.TopicSlugs, topicsBySlug, topicTerms);
                ApplyScores(story, summary, now);
                await ApplyCommitmentAsync(story, context, now, cancellationToken);
                await ApplyComparisonAsync(story, now, cancellationToken);

                story.Status = StoryStatus.Published;
                // Stamped once. From here the permalink is frozen and the detail page
                // keeps serving the story even while a later re-enrichment is running.
                story.FirstPublishedAt ??= now;
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
            // The language the extracted sentences are actually in. The first
            // article is the one the story's title, dek and body come from, so its
            // language is the one that matters; an English story with one Turkish
            // sibling article is still an English story.
            Language = articles.FirstOrDefault()?.Language ?? "en",
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
    /// Turns any field still carrying the article's own language into Turkish.
    /// </summary>
    /// <remarks>
    /// This is the seam that covers both leaks at once. On the no-LLM path the
    /// extractive fallback declares every field it lifted from the article, so the
    /// whole card gets translated. On the LLM path the model writes Turkish, and
    /// only the fields it declined to fill — which the pipeline would otherwise
    /// silently backfill from the source — are declared and translated.
    ///
    /// Nothing here can fail the story. The translator returns the original text
    /// for anything it cannot render safely, so the worst case is exactly the
    /// behaviour that existed before translation was wired in.
    /// </remarks>
    private async Task<StorySummaryResult> TranslateAsync(
        Story story,
        StoryPromptContext context,
        StorySummaryResult summary,
        CancellationToken cancellationToken)
    {
        if (!translator.IsEnabled || !TranslationGuard.NeedsTurkish(context.Language))
        {
            return summary;
        }

        var fields = summary.UntranslatedFields.ToHashSet(StringComparer.Ordinal);

        // Everything below turns on one question: is this the story's first
        // enrichment? Before it, the story's own title and dek are still raw source
        // text. After it, they are this pipeline's settled output — re-translating
        // them would feed Turkish back through the translator, and a title rewritten
        // a little on every re-enrichment drifts away from what it started as.
        var firstEnrichment = story.Summary is null;

        // The dek is the one field the LLM path backfills from the story itself
        // rather than from the result, so the producer cannot declare it.
        if (firstEnrichment &&
            string.IsNullOrWhiteSpace(summary.Dek) &&
            !string.IsNullOrWhiteSpace(story.Dek))
        {
            summary = summary with { Dek = story.Dek };
            fields.Add(SummaryField.Dek);
        }

        // Same reasoning for the title. The producer declares it untranslated when
        // the model omitted one — but on a re-run the value it fell back to is
        // story.Title, which the first run already settled.
        if (!firstEnrichment)
        {
            fields.Remove(SummaryField.Title);
        }

        if (fields.Count == 0)
        {
            return summary;
        }

        // One flat list so the translator sees the whole story's work at once and
        // can budget across it, rather than being called per field.
        var segments = new List<string>();
        var slots = new List<string>();

        void Queue(string field, string? text)
        {
            if (fields.Contains(field) && !string.IsNullOrWhiteSpace(text))
            {
                segments.Add(text);
                slots.Add(field);
            }
        }

        Queue(SummaryField.Title, summary.Title);
        Queue(SummaryField.Dek, summary.Dek);
        Queue(SummaryField.Summary, summary.Summary);
        Queue(SummaryField.WhyItMatters, summary.WhyItMatters);
        Queue(SummaryField.WhoIsAffected, summary.WhoIsAffected);
        Queue(SummaryField.WhatShouldIDo, summary.WhatShouldIDo);

        var keyPointStart = segments.Count;
        if (fields.Contains(SummaryField.KeyPoints))
        {
            foreach (var point in summary.KeyPoints.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                segments.Add(point);
                slots.Add(SummaryField.KeyPoints);
            }
        }

        if (segments.Count == 0)
        {
            return summary;
        }

        IReadOnlyList<string> translated;

        try
        {
            translated = await translator.TranslateAsync(segments, context.Language, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI translation failed for {StorySlug}", story.Slug);
            return summary;
        }

        // A backend that returns the wrong shape is not trusted to have returned
        // the right text either.
        if (translated.Count != segments.Count)
        {
            logger.LogWarning(
                "FocusAI translator returned {Actual} segment(s) for {Expected}; ignoring the result",
                translated.Count,
                segments.Count);

            return summary;
        }

        string Take(string field)
        {
            var index = slots.IndexOf(field);
            return index < 0 ? string.Empty : translated[index];
        }

        var result = summary;

        if (fields.Contains(SummaryField.Title) && Take(SummaryField.Title) is { Length: > 0 } title)
        {
            result = result with { Title = title };
        }

        if (fields.Contains(SummaryField.Dek) && Take(SummaryField.Dek) is { Length: > 0 } dek)
        {
            result = result with { Dek = dek };
        }

        if (fields.Contains(SummaryField.Summary) && Take(SummaryField.Summary) is { Length: > 0 } body)
        {
            result = result with { Summary = body };
        }

        if (fields.Contains(SummaryField.WhyItMatters) && Take(SummaryField.WhyItMatters) is { Length: > 0 } why)
        {
            result = result with { WhyItMatters = why };
        }

        if (fields.Contains(SummaryField.WhoIsAffected) && Take(SummaryField.WhoIsAffected) is { Length: > 0 } who)
        {
            result = result with { WhoIsAffected = who };
        }

        if (fields.Contains(SummaryField.WhatShouldIDo) && Take(SummaryField.WhatShouldIDo) is { Length: > 0 } todo)
        {
            result = result with { WhatShouldIDo = todo };
        }

        if (fields.Contains(SummaryField.KeyPoints) && segments.Count > keyPointStart)
        {
            result = result with
            {
                KeyPoints = translated.Skip(keyPointStart).ToList()
            };
        }

        return result with { UntranslatedFields = [] };
    }

    /// <summary>
    /// The source text a displayed term has to be found in. Deliberately the
    /// articles' own words rather than the model's summary — grounding generated
    /// text against other generated text proves nothing.
    /// </summary>
    private static string SourceCorpus(Story story) =>
        string.Join(' ', story.Articles.Select(a => $"{a.Title} {a.BestText()}"));

    /// <summary>
    /// Finance stories carry a commitment classification; everything else does not.
    /// A failure here must not fail the story — the item simply never reaches the
    /// Finans feed, which is the safe direction.
    /// </summary>
    private async Task ApplyCommitmentAsync(
        Story story,
        StoryPromptContext context,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (story.Category != ContentCategory.Finance)
        {
            return;
        }

        // Deterministic and unconditional: this is what stands in for the classifier
        // when no model is configured, so it must not depend on the call below
        // succeeding. Reads the text the reader will actually see.
        story.HasEvidentialClaim = CommitmentLexicon.HasEvidentialSuffix(
            CommitmentLexicon.Fold($"{story.Title} {story.Dek} {story.Summary?.Summary}"));

        StoryCommitment? evaluated;

        try
        {
            var result = await commitmentClassifier.ClassifyAsync(context, cancellationToken);
            evaluated = CommitmentEvaluator.Evaluate(
                result,
                SourceCorpus(story),
                story.PublishedAt,
                now,
                commitmentClassifier.Version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI commitment classification failed for {StorySlug}", story.Slug);
            evaluated = null;
        }

        if (evaluated is null)
        {
            // An existing classification is cleared rather than left behind: stale
            // certainty is worse than none.
            if (story.Commitment is not null)
            {
                db.StoryCommitments.Remove(story.Commitment);
                story.Commitment = null;
            }

            return;
        }

        if (story.Commitment is null)
        {
            evaluated.StoryId = story.Id;
            story.Commitment = evaluated;
            db.StoryCommitments.Add(evaluated);
            return;
        }

        var existing = story.Commitment;
        existing.Tier = evaluated.Tier;
        existing.ModelTier = evaluated.ModelTier;
        existing.Horizon = evaluated.Horizon;
        existing.ClaimSource = evaluated.ClaimSource;
        existing.Instrument = evaluated.Instrument;
        existing.Event = evaluated.Event;
        existing.DateText = evaluated.DateText;
        existing.EventDate = evaluated.EventDate;
        existing.DatePrecision = evaluated.DatePrecision;
        existing.Quote = evaluated.Quote;
        existing.Condition = evaluated.Condition;
        existing.Reference = evaluated.Reference;
        existing.IsReversed = evaluated.IsReversed;
        existing.ClassifierVersion = evaluated.ClassifierVersion;
        existing.ClassifiedAt = evaluated.ClassifiedAt;
        existing.UpdatedAt = now;
    }

    /// <summary>
    /// Compares how the member outlets covered the story. Only meaningful with two
    /// or more sources, and a failure is silent — the detail page still shows the
    /// deterministic timeline and each outlet's own headline.
    /// </summary>
    private async Task ApplyComparisonAsync(Story story, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var sources = story.Articles
            .OrderBy(a => a.PublishedAt)
            .Select(a => new CoverageSource(
                a.Source?.Name ?? "Bilinmeyen kaynak",
                a.Title,
                a.BestText()))
            .ToList();

        if (sources.Count < 2)
        {
            return;
        }

        StoryComparison? evaluated;

        try
        {
            var result = await coverageComparer.CompareAsync(story.Title, sources, cancellationToken);
            evaluated = CoverageEvaluator.Evaluate(result, sources, now, coverageComparer.Version);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI coverage comparison failed for {StorySlug}", story.Slug);
            evaluated = null;
        }

        if (evaluated is null)
        {
            return;
        }

        if (story.Comparison is null)
        {
            evaluated.StoryId = story.Id;
            story.Comparison = evaluated;
            db.StoryComparisons.Add(evaluated);
            return;
        }

        story.Comparison.Points = evaluated.Points;
        story.Comparison.Provider = evaluated.Provider;
        story.Comparison.Model = evaluated.Model;
        story.Comparison.ClassifierVersion = evaluated.ClassifierVersion;
        story.Comparison.GeneratedAt = evaluated.GeneratedAt;
        story.Comparison.UpdatedAt = now;
    }

    private void ApplySummary(Story story, StorySummaryResult result, DateTimeOffset now, bool wasPublished)
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

            // The slug is a permalink, so it may only change while nobody could
            // have linked to the story — which is exactly until it first publishes.
            //
            // The previous condition also required an empty slug, which clustering
            // never leaves behind, so it never fired: every story kept the slug
            // minted from its source-language headline forever. Minting here is
            // what makes the URL match the title the reader actually sees. It is
            // deterministic in (title, id), so a retried enrichment lands on the
            // same slug rather than churning.
            if (!wasPublished)
            {
                story.Slug = Slugger.SlugifyUnique(story.Title, story.Id);
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
