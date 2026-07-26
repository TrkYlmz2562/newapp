using System.Text;
using System.Text.Json;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Entities.Ai;
using FocusAI.Domain.Enums;
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
    public async Task<StorySummaryResult> SummarizeAsync(
        StoryPromptContext context,
        CancellationToken cancellationToken = default)
    {
        var client = clientFactory.Create();
        if (!client.IsEnabled)
        {
            return ExtractiveFallback.Summarize(context);
        }

        var response = await SendAsync(
            client,
            new LlmRequest
            {
                SystemPrompt = Prompts.SummarySystem,
                Messages = [LlmMessage.User(BuildStoryPrompt(context))],
                JsonMode = true,
                // Headroom for the visual fields: a response truncated mid-JSON
                // does not lose just those keys, it drops the whole summary to
                // the extractive fallback.
                MaxTokens = 1500,
                Operation = "summary"
            },
            cancellationToken);

        var json = JsonExtractor.TryExtract(response.Content);
        if (!response.Succeeded || json is not { } root)
        {
            logger.LogWarning(
                "FocusAI summary fell back to extractive mode for '{Title}': {Error}",
                context.Title,
                response.Error ?? "unparseable JSON");

            return ExtractiveFallback.Summarize(context);
        }

        var summary = JsonExtractor.GetString(root, "summary");
        if (string.IsNullOrWhiteSpace(summary))
        {
            return ExtractiveFallback.Summarize(context);
        }

        return new StorySummaryResult
        {
            Title = JsonExtractor.GetString(root, "title") ?? context.Title,
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
            Provider = client.Provider.ToString(),
            Model = response.Model,
            PromptTokens = response.PromptTokens,
            CompletionTokens = response.CompletionTokens
        };
    }

    public async Task<StoryAnalysisResult> AnalyzeAsync(
        StoryPromptContext context,
        CancellationToken cancellationToken = default)
    {
        var client = clientFactory.Create();
        if (!client.IsEnabled)
        {
            return ExtractiveFallback.Analyze(context);
        }

        var response = await SendAsync(
            client,
            new LlmRequest
            {
                SystemPrompt = Prompts.AnalysisSystem,
                Messages = [LlmMessage.User(BuildStoryPrompt(context))],
                JsonMode = true,
                MaxTokens = 1200,
                Temperature = 0.3,
                Operation = "analysis"
            },
            cancellationToken);

        var json = JsonExtractor.TryExtract(response.Content);
        if (!response.Succeeded || json is not { } root)
        {
            return ExtractiveFallback.Analyze(context);
        }

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
