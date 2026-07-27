using System.Text;
using System.Text.Json;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Common;
using FocusAI.Domain.Entities.Ai;
using FocusAI.Domain.Enums;
using FocusAI.Domain.Learning;
using FocusAI.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Implements every AI operation the product needs on top of whichever provider
/// is configured, logging usage and degrading to <see cref="ExtractiveFallback"/>
/// whenever the model is unavailable or returns something unparseable.
/// </summary>
public sealed class ContentAiService(
    ILlmClientFactory clientFactory,
    ApplicationDbContext db,
    IDateTimeProvider clock,
    ILogger<ContentAiService> logger) : IContentAiService
{
    public async Task<StoryEnrichmentResult> EnrichAsync(
        StoryPromptContext context,
        CancellationToken cancellationToken = default)
    {
        var client = clientFactory.Create();
        if (!client.IsEnabled)
        {
            return new StoryEnrichmentResult(
                ExtractiveFallback.Summarize(context),
                ExtractiveFallback.Analyze(context));
        }

        var response = await SendAsync(
            client,
            new LlmRequest
            {
                SystemPrompt = Prompts.EnrichSystem,
                Messages = [LlmMessage.User(BuildStoryPrompt(context))],
                JsonMode = true,
                // The old two budgets added together, plus a little. Headroom
                // matters more here than it did apart: a response truncated
                // mid-JSON does not lose the tail keys, it loses both halves.
                MaxTokens = 2600,
                // The analysis half asked for 0.3 and the summary half took the
                // default. Judgement is the part that suffers from invention, so
                // the lower of the two wins.
                Temperature = 0.3,
                Operation = "enrich"
            },
            cancellationToken);

        var json = JsonExtractor.TryExtract(response.Content);
        if (!response.Succeeded || json is not { } root)
        {
            logger.LogWarning(
                "FocusAI enrichment fell back to extractive mode for '{Title}': {Error}",
                context.Title,
                response.Error ?? "unparseable JSON");

            return new StoryEnrichmentResult(
                ExtractiveFallback.Summarize(context),
                ExtractiveFallback.Analyze(context));
        }

        // Each half is read independently, and each falls back on its own. A model
        // that writes a clean summary and then fumbles the verdict should cost the
        // story its verdict, not its summary.
        return new StoryEnrichmentResult(
            ParseSummary(Section(root, "summary"), context, client, response),
            ParseAnalysis(Section(root, "analysis"), context, client, response));
    }

    /// <summary>
    /// Pulls one half out of the combined response.
    /// </summary>
    /// <remarks>
    /// Falls back to the whole object when the wrapper key is missing, which is the
    /// shape a model produces when it ignores the nesting instruction and answers
    /// one of the two schemas flat. The keys do not collide between the halves, so
    /// reading a flat object as either half finds whatever it did answer and lets
    /// the other half fall back on its own.
    /// </remarks>
    private static JsonElement Section(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty(name, out var section) &&
        section.ValueKind == JsonValueKind.Object
            ? section
            : root;

    private static StorySummaryResult ParseSummary(
        JsonElement root,
        StoryPromptContext context,
        ILlmClient client,
        LlmResponse response)
    {
        var summary = JsonExtractor.GetString(root, "summary");
        if (string.IsNullOrWhiteSpace(summary))
        {
            return ExtractiveFallback.Summarize(context);
        }

        // The prompt asks for a Turkish title, but nothing can force the model to
        // return one. When it omits the key the story would otherwise be published
        // under the source headline, so the borrowed field is declared rather than
        // silently passed off as model output.
        // Whitespace counts as omitted: a model that answers "title": " " would
        // otherwise publish the source headline while declaring the result Turkish.
        var modelTitle = JsonExtractor.GetString(root, "title");
        var hasModelTitle = !string.IsNullOrWhiteSpace(modelTitle);

        return new StorySummaryResult
        {
            Title = hasModelTitle ? modelTitle! : context.Title,
            Dek = JsonExtractor.GetString(root, "dek"),
            Summary = summary,
            WhyItMatters = JsonExtractor.GetString(root, "whyItMatters"),
            WhoIsAffected = JsonExtractor.GetString(root, "whoIsAffected"),
            WhatShouldIDo = JsonExtractor.GetString(root, "whatShouldIDo"),
            KeyPoints = JsonExtractor.GetStringList(root, "keyPoints"),
            TopicSlugs = JsonExtractor.GetStringList(root, "topicSlugs"),
            Category = JsonExtractor.GetEnum(root, "category", ContentCategory.Unknown),
            Importance = Math.Clamp(JsonExtractor.GetDouble(root, "importance", 0.5), 0d, 1d),
            TechnicalAccuracy = Math.Clamp(JsonExtractor.GetDouble(root, "technicalAccuracy", 0.6), 0d, 1d),
            ReadingMinutes = Math.Clamp(JsonExtractor.GetInt(root, "readingMinutes", 2), 1, 15),
            VisualEntity = JsonExtractor.GetString(root, "visualEntity"),
            UntranslatedFields = hasModelTitle ? [] : [SummaryField.Title],
            Provider = client.Provider.ToString(),
            Model = response.Model,
            PromptTokens = response.PromptTokens,
            CompletionTokens = response.CompletionTokens
        };
    }

    private static StoryAnalysisResult ParseAnalysis(
        JsonElement root,
        StoryPromptContext context,
        ILlmClient client,
        LlmResponse response)
    {
        var why = JsonExtractor.GetString(root, "whyImportant");
        if (string.IsNullOrWhiteSpace(why))
        {
            return ExtractiveFallback.Analyze(context);
        }

        return new StoryAnalysisResult
        {
            WhyImportant = why,
            RealImpact = JsonExtractor.GetString(root, "realImpact"),
            Hype = JsonExtractor.GetEnum(root, "hype", HypeLevel.Accurate),
            HypeReasoning = JsonExtractor.GetString(root, "hypeReasoning"),
            LearnUrgency = JsonExtractor.GetEnum(root, "learnUrgency", LearnUrgency.Watch),
            Longevity = JsonExtractor.GetEnum(root, "longevity", LongevityOutlook.Uncertain),
            LongevityReasoning = JsonExtractor.GetString(root, "longevityReasoning"),
            StackNotes = JsonExtractor.GetStringMap(root, "stackNotes"),
            Confidence = Math.Clamp(JsonExtractor.GetDouble(root, "confidence", 0.5), 0d, 1d),
            Provider = client.Provider.ToString(),
            Model = response.Model
        };
    }

    public async Task<ParsedSearchQuery> ParseSearchQueryAsync(
        string rawQuery,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var client = clientFactory.Create();
        if (!client.IsEnabled)
        {
            return ExtractiveFallback.ParseQuery(rawQuery, now);
        }

        var response = await SendAsync(
            client,
            new LlmRequest
            {
                SystemPrompt = Prompts.SearchParseSystem,
                Messages =
                [
                    LlmMessage.User($"Bugünün tarihi: {now:yyyy-MM-dd}\nSorgu: {rawQuery}")
                ],
                JsonMode = true,
                MaxTokens = 400,
                Temperature = 0d,
                Operation = "search-parse"
            },
            cancellationToken);

        var json = JsonExtractor.TryExtract(response.Content);
        if (!response.Succeeded || json is not { } root)
        {
            return ExtractiveFallback.ParseQuery(rawQuery, now);
        }

        var minTrust = JsonExtractor.GetInt(root, "minTrustScore", -1);

        return new ParsedSearchQuery
        {
            Text = JsonExtractor.GetString(root, "text") ?? rawQuery,
            TopicSlugs = JsonExtractor.GetStringList(root, "topicSlugs"),
            Category = root.TryGetProperty("category", out var category) &&
                       category.ValueKind == JsonValueKind.String &&
                       Enum.TryParse<ContentCategory>(category.GetString(), ignoreCase: true, out var parsed)
                ? parsed
                : null,
            From = JsonExtractor.GetDate(root, "from"),
            To = JsonExtractor.GetDate(root, "to"),
            MinTrustScore = minTrust is >= 0 and <= 100 ? minTrust : null,
            OfficialSourcesOnly = JsonExtractor.GetBool(root, "officialSourcesOnly", false),
            ParsedByLlm = true
        };
    }

    public async Task<string> WriteDigestIntroAsync(
        IReadOnlyList<string> headlines,
        string language,
        CancellationToken cancellationToken = default)
    {
        var client = clientFactory.Create();
        if (!client.IsEnabled || headlines.Count == 0)
        {
            return ExtractiveFallback.DigestIntro(headlines);
        }

        var prompt = new StringBuilder("Bugünün başlıkları:\n");
        foreach (var headline in headlines.Take(15))
        {
            prompt.AppendLine($"- {headline}");
        }

        var response = await SendAsync(
            client,
            new LlmRequest
            {
                SystemPrompt = Prompts.DigestIntroSystem,
                Messages = [LlmMessage.User(prompt.ToString())],
                MaxTokens = 200,
                Temperature = 0.4,
                Operation = "digest-intro"
            },
            cancellationToken);

        if (!response.Succeeded || string.IsNullOrWhiteSpace(response.Content))
        {
            return ExtractiveFallback.DigestIntro(headlines);
        }

        var text = response.Content.Trim();
        return text.Length <= 400 ? text : text[..400];
    }

    public async Task<LearningSuggestionResult?> SuggestLearningAsync(
        IReadOnlyList<string> interestSlugs,
        IReadOnlyList<string> recentHeadlines,
        int minutes,
        string language,
        CancellationToken cancellationToken = default)
    {
        var client = clientFactory.Create();
        if (!client.IsEnabled)
        {
            // Inventing a study plan without a model would mean inventing links.
            return null;
        }

        var prompt = new StringBuilder();
        prompt.AppendLine($"Günlük süre: {minutes} dakika");
        prompt.AppendLine($"İlgi alanları: {(interestSlugs.Count > 0 ? string.Join(", ", interestSlugs) : "belirtilmemiş")}");
        prompt.AppendLine("Son bir haftanın öne çıkan başlıkları:");
        foreach (var headline in recentHeadlines.Take(20))
        {
            prompt.AppendLine($"- {headline}");
        }

        var response = await SendAsync(
            client,
            new LlmRequest
            {
                SystemPrompt = Prompts.LearningSystem,
                Messages = [LlmMessage.User(prompt.ToString())],
                JsonMode = true,
                MaxTokens = 900,
                Temperature = 0.4,
                Operation = "learning"
            },
            cancellationToken);

        var json = JsonExtractor.TryExtract(response.Content);
        if (!response.Succeeded || json is not { } root)
        {
            return null;
        }

        var title = JsonExtractor.GetString(root, "title");
        var rationale = JsonExtractor.GetString(root, "rationale");

        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(rationale))
        {
            return null;
        }

        var resources = new List<LearningResourceResult>();
        if (root.TryGetProperty("resources", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray().Take(5))
            {
                var resourceTitle = JsonExtractor.GetString(item, "title");
                var url = JsonExtractor.GetString(item, "url");

                // Drop anything that is not a well-formed absolute http(s) URL —
                // a hallucinated link is worse than no link.
                if (string.IsNullOrWhiteSpace(resourceTitle) ||
                    !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                {
                    continue;
                }

                resources.Add(new LearningResourceResult(
                    resourceTitle,
                    uri.ToString(),
                    JsonExtractor.GetString(item, "kind") ?? "article",
                    Math.Clamp(JsonExtractor.GetInt(item, "estimatedMinutes", 5), 1, 120)));
            }
        }

        return new LearningSuggestionResult
        {
            Title = title,
            Rationale = rationale,
            TopicSlug = JsonExtractor.GetString(root, "topicSlug"),
            EstimatedMinutes = Math.Clamp(JsonExtractor.GetInt(root, "estimatedMinutes", minutes), 5, 120),
            Resources = resources
        };
    }

    public async Task<BriefPlan?> PlanBriefAsync(
        string caseFile,
        int minutes,
        CancellationToken cancellationToken = default)
    {
        var client = clientFactory.Create();
        if (!client.IsEnabled)
        {
            // No extractive fallback here on purpose. Anchor questions derived from
            // the text by rule would be "X nedir?" — the exact generic output the
            // brief exists to avoid — and the case file alone is already a usable
            // lesson, so the caller ships that and says the planner did not run.
            return null;
        }

        var prompt = new StringBuilder();
        prompt.AppendLine($"Kullanıcının bu derse ayırdığı süre: {minutes} dakika");
        prompt.AppendLine();
        prompt.AppendLine("DERS DOSYASI:");
        prompt.AppendLine(caseFile);

        var response = await SendAsync(
            client,
            new LlmRequest
            {
                SystemPrompt = Prompts.BriefPlanSystem,
                Messages = [LlmMessage.User(prompt.ToString())],
                JsonMode = true,
                MaxTokens = 1200,
                // Warmer than the summariser: the questions are meant to be the
                // non-obvious ones, and at 0.2 the model reaches for the definition
                // question every time.
                Temperature = 0.5,
                Operation = "brief-plan"
            },
            cancellationToken);

        var json = JsonExtractor.TryExtract(response.Content);
        if (!response.Succeeded || json is not { } root)
        {
            logger.LogWarning(
                "FocusAI brief planner returned nothing usable: {Error}",
                response.Error ?? "unparseable JSON");

            return null;
        }

        var questions = JsonExtractor.GetStringList(root, "anchorQuestions")
            .Select(q => q.Trim())
            .Where(q => q.Length > 0)
            .Take(6)
            .ToList();

        var goal = JsonExtractor.GetString(root, "learningGoal");

        // A plan with neither a goal nor a question is not a plan; treating it as
        // one would put an empty "Bu derste" heading in the brief.
        if (string.IsNullOrWhiteSpace(goal) && questions.Count == 0)
        {
            return null;
        }

        return new BriefPlan
        {
            LearningGoal = FieldLimits.Cap(goal, 500),
            EntryLevel = Math.Clamp(JsonExtractor.GetInt(root, "entryLevel", 1), 0, 4),
            EntryReason = FieldLimits.Cap(JsonExtractor.GetString(root, "entryReason"), 300),
            DemoIdea = FieldLimits.Cap(NullIfLiteralNull(JsonExtractor.GetString(root, "demoIdea")), 500),
            DiagramIdea = FieldLimits.Cap(NullIfLiteralNull(JsonExtractor.GetString(root, "diagramIdea")), 500),
            AnchorQuestions = questions,
            CommonMistake = FieldLimits.Cap(JsonExtractor.GetString(root, "commonMistake"), 500),
            OpenQuestion = FieldLimits.Cap(JsonExtractor.GetString(root, "openQuestion"), 500),
            Provider = client.Provider.ToString(),
            Model = response.Model
        };
    }

    /// <summary>
    /// The prompt offers "null" as a value for the optional ideas, and models
    /// oblige by sending the four-character string rather than a JSON null.
    /// </summary>
    private static string? NullIfLiteralNull(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Equals("null", StringComparison.OrdinalIgnoreCase)
            ? null
            : value;

    public async Task<AskAnswer> AnswerAsync(
        string question,
        IReadOnlyList<(Guid StoryId, string Title, string Summary)> context,
        string language,
        CancellationToken cancellationToken = default)
    {
        var client = clientFactory.Create();
        if (!client.IsEnabled)
        {
            return new AskAnswer
            {
                Answer = "Soru-cevap için yapılandırılmış bir yapay zekâ sağlayıcısı yok. " +
                         "Aşağıdaki ilgili haberlere göz atabilirsin.",
                CitedStoryIds = context.Take(3).Select(c => c.StoryId).ToList(),
                Confidence = 0d
            };
        }

        var prompt = new StringBuilder();
        prompt.AppendLine($"Soru: {question}");
        prompt.AppendLine();
        prompt.AppendLine("Bağlam:");
        foreach (var (storyId, title, summary) in context)
        {
            prompt.AppendLine($"[{storyId}] {title}");
            prompt.AppendLine(summary);
            prompt.AppendLine();
        }

        var response = await SendAsync(
            client,
            new LlmRequest
            {
                SystemPrompt = Prompts.AskSystem,
                Messages = [LlmMessage.User(prompt.ToString())],
                JsonMode = true,
                MaxTokens = 1200,
                Temperature = 0.2,
                Operation = "ask"
            },
            cancellationToken);

        var json = JsonExtractor.TryExtract(response.Content);
        if (!response.Succeeded || json is not { } root)
        {
            return new AskAnswer
            {
                Answer = "Şu anda bu soruya yanıt üretilemedi. Lütfen daha sonra tekrar dene.",
                CitedStoryIds = [],
                Confidence = 0d
            };
        }

        var citedIds = JsonExtractor.GetStringList(root, "citedStoryIds")
            .Select(id => Guid.TryParse(id, out var parsed) ? parsed : Guid.Empty)
            .Where(id => id != Guid.Empty)
            // Only ids actually present in the retrieved context count as citations.
            .Where(id => context.Any(c => c.StoryId == id))
            .ToList();

        return new AskAnswer
        {
            Answer = JsonExtractor.GetString(root, "answer") ?? "Yanıt üretilemedi.",
            CitedStoryIds = citedIds,
            Confidence = Math.Clamp(JsonExtractor.GetDouble(root, "confidence", 0.5), 0d, 1d)
        };
    }

    private static string BuildStoryPrompt(StoryPromptContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Başlık: {context.Title}");
        builder.AppendLine($"Yayın tarihi: {context.PublishedAt:yyyy-MM-dd}");
        builder.AppendLine($"Kaynaklar: {string.Join(", ", context.SourceNames)}");
        builder.AppendLine();
        builder.AppendLine("Kaynak metinleri:");

        var index = 1;
        foreach (var excerpt in context.ArticleExcerpts)
        {
            builder.AppendLine($"--- Kaynak {index++} ---");
            builder.AppendLine(excerpt);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>
    /// Sends the request and records the call in the usage ledger. Ledger writes
    /// are best-effort: a metering failure must never fail the user's request.
    /// </summary>
    private async Task<LlmResponse> SendAsync(
        ILlmClient client,
        LlmRequest request,
        CancellationToken cancellationToken)
    {
        var response = await client.CompleteAsync(request, cancellationToken);

        try
        {
            db.AiUsageLogs.Add(new AiUsageLog
            {
                Provider = response.Provider,
                Model = response.Model,
                Operation = request.Operation,
                PromptTokens = response.PromptTokens,
                CompletionTokens = response.CompletionTokens,
                LatencyMs = response.LatencyMs,
                Succeeded = response.Succeeded,
                Error = response.Error,
                OccurredAt = clock.UtcNow
            });
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FocusAI failed to record AI usage for {Operation}", request.Operation);
        }

        return response;
    }
}
