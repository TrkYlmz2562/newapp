using System.Text;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Entities.Ai;
using FocusAI.Domain.Enums;
using FocusAI.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Asks the model where a finance story sits on the commitment scale. There is no
/// extractive fallback on purpose: with no model there is no classification, and
/// with no classification nothing enters the Finans feed. A smaller true feed is
/// the correct failure mode for this section.
/// </summary>
public sealed class CommitmentClassifier(
    ILlmClientFactory clientFactory,
    ApplicationDbContext db,
    IDateTimeProvider clock,
    ILogger<CommitmentClassifier> logger) : ICommitmentClassifier
{
    public int Version => Prompts.CommitmentClassifierVersion;

    public async Task<CommitmentResult> ClassifyAsync(
        StoryPromptContext context,
        CancellationToken cancellationToken = default)
    {
        var client = clientFactory.Create();
        if (!client.IsEnabled)
        {
            return CommitmentResult.NotClassified;
        }

        var request = new LlmRequest
        {
            SystemPrompt = Prompts.CommitmentSystem,
            Messages = [LlmMessage.User(BuildPrompt(context))],
            JsonMode = true,
            MaxTokens = 900,
            // Deterministic: this output decides what a reader is told is certain.
            Temperature = 0d,
            Operation = "commitment"
        };

        var response = await client.CompleteAsync(request, cancellationToken);
        RecordUsage(request, response);

        var json = JsonExtractor.TryExtract(response.Content);
        if (!response.Succeeded || json is not { } root)
        {
            logger.LogDebug(
                "FocusAI commitment classification unavailable for '{Title}': {Error}",
                context.Title,
                response.Error ?? "unparseable JSON");

            return CommitmentResult.NotClassified;
        }

        return new CommitmentResult
        {
            Succeeded = true,
            IsFinance = JsonExtractor.GetBool(root, "isFinance", false),
            Tier = ParseTier(JsonExtractor.GetString(root, "tier")),
            ClaimSource = ParseClaimSource(JsonExtractor.GetString(root, "claimSource")),
            Instrument = ParseInstrument(JsonExtractor.GetString(root, "instrument")),
            Event = JsonExtractor.GetString(root, "event"),
            DateText = JsonExtractor.GetString(root, "dateText"),
            Quote = JsonExtractor.GetString(root, "quote"),
            Condition = JsonExtractor.GetString(root, "condition"),
            Reference = JsonExtractor.GetString(root, "reference"),
            // Absent means ambiguous: the safe reading, since the field's whole
            // purpose is to let the model decline.
            Ambiguous = JsonExtractor.GetBool(root, "ambiguous", true)
        };
    }

    /// <summary>
    /// Same ledger as every other call, so this section's cost is visible in
    /// ai_usage_logs rather than being an untracked extra per finance story.
    /// </summary>
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
            logger.LogWarning(ex, "FocusAI failed to record AI usage for commitment classification");
        }
    }

    private static string BuildPrompt(StoryPromptContext context)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Yayın tarihi: {context.PublishedAt:yyyy-MM-dd}");
        builder.AppendLine($"Başlık: {context.Title}");
        builder.AppendLine();

        foreach (var excerpt in context.ArticleExcerpts.Take(4))
        {
            builder.AppendLine(excerpt);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    // Unrecognised values fall to the least-certain reading rather than to a
    // default that would let an unparsed answer through the gate.
    private static CommitmentTier ParseTier(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "REALIZED" => CommitmentTier.Realized,
        "ENACTED_DATED" => CommitmentTier.EnactedDated,
        "OFFICIAL_COMMITMENT" => CommitmentTier.OfficialCommitment,
        "CONDITIONAL_PENDING" => CommitmentTier.ConditionalPending,
        "STATED_INTENT" => CommitmentTier.StatedIntent,
        "UNVERIFIED_CLAIM" => CommitmentTier.UnverifiedClaim,
        "ANALYST_SPECULATION" => CommitmentTier.AnalystSpeculation,
        _ => CommitmentTier.Unknown
    };

    private static ClaimSource ParseClaimSource(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "OFFICIAL_DOCUMENT" => ClaimSource.OfficialDocument,
        "ACTOR_ITSELF" => ClaimSource.ActorItself,
        "NAMED_THIRD_PARTY" => ClaimSource.NamedThirdParty,
        "UNNAMED_SOURCE" => ClaimSource.UnnamedSource,
        "OUTLET_INFERENCE" => ClaimSource.OutletInference,
        _ => ClaimSource.Unknown
    };

    private static FinanceInstrument ParseInstrument(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "RESMI_GAZETE" => FinanceInstrument.ResmiGazete,
        "KAP" => FinanceInstrument.Kap,
        "KURUM_KARARI" => FinanceInstrument.KurumKarari,
        "MAHKEME" => FinanceInstrument.Mahkeme,
        "SOZLESME" => FinanceInstrument.Sozlesme,
        "RESMI_TAKVIM" => FinanceInstrument.ResmiTakvim,
        _ => FinanceInstrument.None
    };
}
