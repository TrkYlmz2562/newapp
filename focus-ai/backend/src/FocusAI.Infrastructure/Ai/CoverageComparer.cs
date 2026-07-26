using System.Text;
using System.Text.Json;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Entities.Ai;
using FocusAI.Domain.Enums;
using FocusAI.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Asks the model where the outlets covering one story agree and differ. There is
/// no extractive fallback: without a model there is no comparison, and the detail
/// page simply shows the deterministic parts (who published when, and each
/// outlet's own headline), which are worth reading on their own.
/// </summary>
public sealed class CoverageComparer(
    ILlmClientFactory clientFactory,
    ApplicationDbContext db,
    IDateTimeProvider clock,
    ILogger<CoverageComparer> logger) : ICoverageComparer
{
    /// <summary>Enough context to compare on, without paying for whole articles.</summary>
    private const int ExcerptLength = 1800;

    private const int MaxSources = 5;

    public int Version => Prompts.ComparisonClassifierVersion;

    public async Task<CoverageComparisonResult> CompareAsync(
        string title,
        IReadOnlyList<CoverageSource> sources,
        CancellationToken cancellationToken = default)
    {
        if (sources.Count < 2)
        {
            return CoverageComparisonResult.None;
        }

        var client = clientFactory.Create();
        if (!client.IsEnabled)
        {
            return CoverageComparisonResult.None;
        }

        var request = new LlmRequest
        {
            SystemPrompt = Prompts.ComparisonSystem,
            Messages = [LlmMessage.User(BuildPrompt(title, sources))],
            JsonMode = true,
            MaxTokens = 1200,
            Temperature = 0.2,
            Operation = "comparison"
        };

        var response = await client.CompleteAsync(request, cancellationToken);
        RecordUsage(request, response);

        var json = JsonExtractor.TryExtract(response.Content);
        if (!response.Succeeded || json is not { } root ||
            !root.TryGetProperty("points", out var points) || points.ValueKind != JsonValueKind.Array)
        {
            logger.LogDebug(
                "FocusAI coverage comparison unavailable for '{Title}': {Error}",
                title,
                response.Error ?? "unparseable JSON");

            return CoverageComparisonResult.None;
        }

        var parsed = new List<CoveragePointResult>();

        foreach (var element in points.EnumerateArray())
        {
            var text = JsonExtractor.GetString(element, "text");
            var quote = JsonExtractor.GetString(element, "quote");
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(quote))
            {
                continue;
            }

            parsed.Add(new CoveragePointResult(
                text,
                ParseKind(JsonExtractor.GetString(element, "kind")),
                ReadIndexes(element),
                quote,
                JsonExtractor.GetInt(element, "quoteSource", -1)));
        }

        return new CoverageComparisonResult
        {
            Succeeded = true,
            Points = parsed,
            Provider = client.Provider.ToString(),
            Model = response.Model
        };
    }

    private static IReadOnlyList<int> ReadIndexes(JsonElement element)
    {
        if (!element.TryGetProperty("sources", out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return array
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Number)
            .Select(item => item.GetInt32())
            .ToList();
    }

    private static string BuildPrompt(string title, IReadOnlyList<CoverageSource> sources)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Gelişme: {title}");
        builder.AppendLine();

        for (var index = 0; index < Math.Min(sources.Count, MaxSources); index++)
        {
            var source = sources[index];
            var text = source.Text.Length <= ExcerptLength ? source.Text : source.Text[..ExcerptLength];

            builder.AppendLine($"[{index}] {source.SourceName}");
            builder.AppendLine($"Başlık: {source.ArticleTitle}");
            builder.AppendLine(text);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static ComparisonKind ParseKind(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "DIVERGENT" => ComparisonKind.Divergent,
        "UNIQUE" => ComparisonKind.Unique,
        _ => ComparisonKind.Shared
    };

    private void RecordUsage(LlmRequest request, LlmResponse response)
    {
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
            logger.LogWarning(ex, "FocusAI failed to record AI usage for coverage comparison");
        }
    }
}
