using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Text;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Translates into Turkish using a self-hosted model behind an OpenAI-compatible
/// chat endpoint — in practice llama.cpp's server with a small multilingual
/// translation model.
/// </summary>
/// <remarks>
/// Two things make this safe to publish from. First, every candidate goes through
/// <see cref="TranslationGuard"/>, which refuses anything that loses a version
/// number, a product name or a CVE id — the failure that makes small translation
/// models unusable on tech news. Second, refusal is not an error: the original
/// text is returned and the story ships in the source language, exactly as it did
/// before this class existed.
///
/// No row is written to the AI usage ledger. That ledger exists to attribute spend
/// to a paid provider, and a local model has no spend to attribute; adding rows
/// with zero cost would only dilute it.
/// </remarks>
public sealed class LocalTranslator(
    HttpClient httpClient,
    IOptions<TranslationOptions> options,
    ILogger<LocalTranslator> logger) : IContentTranslator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly TranslationOptions _options = options.Value;

    /// <summary>
    /// Adapted from the model card's structured-data instruction. The explicit
    /// do-not-touch list is why identifiers survive at all — without it the model
    /// helpfully "localises" product names, and every segment then fails the guard.
    /// </summary>
    private const string SystemPrompt =
        "You are a professional English-to-Turkish translator for a technology news site.\n" +
        "Translate the user's text into natural, fluent Turkish.\n" +
        "\n" +
        "Rules:\n" +
        "- Output ONLY the translation. No preamble, no notes, no quotes around it.\n" +
        "- Never translate or transliterate: product names, company names, version " +
        "numbers, CVE identifiers, file names, CLI flags, code, URLs, acronyms. " +
        "Copy them character for character.\n" +
        "- Use the Turkish that Turkish developers actually use: \"yayınlandı\" for " +
        "released, \"hata\" for bug, \"sürüm\" for version, \"açık\" for vulnerability. " +
        "Keep established English terms English when that is what is said in Turkish " +
        "(framework, deploy, commit, pull request).\n" +
        "- Keep every number, percentage, date and unit exactly as given.\n" +
        "- Preserve the sentence count and the meaning. Do not summarise, expand or " +
        "add anything the source does not say.";

    public bool IsEnabled => _options.Enabled && !string.IsNullOrWhiteSpace(_options.BaseUrl);

    public string Backend => IsEnabled ? $"local:{_options.Model}" : "none";

    public async Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> segments,
        string sourceLanguage,
        CancellationToken cancellationToken = default)
    {
        if (!IsEnabled || segments.Count == 0)
        {
            return segments;
        }

        // Seeded with the sources up front, not per iteration: the loop can break
        // early on an exhausted budget, and anything past that point must still
        // come back as its original text rather than as a null the caller would
        // write into a story field.
        var results = segments.ToArray();
        var budget = Math.Max(1, _options.MaxRequestsPerStory);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        var deadlineSeconds = Math.Max(10, _options.MaxSecondsPerStory);
        var refused = 0;
        var translated = 0;

        for (var i = 0; i < segments.Count; i++)
        {
            var source = segments[i];

            // Second gate, on the text rather than the language column. The column
            // is copied from the source registration at ingestion, so every article
            // stored under a mis-registered source carries the wrong language
            // forever — reading the text is what catches those rows.
            if (string.IsNullOrWhiteSpace(source) || TranslationGuard.LooksTurkish(source))
            {
                continue;
            }

            var chunks = TranslationGuard.Chunk(source, Math.Max(120, _options.MaxSegmentChars));

            // Time is the bound that matters: enrichment walks stories one at a
            // time inside a job the scheduler restarts every 20 minutes, so a story
            // that keeps the translator for an hour delays every story behind it.
            var outOfTime = deadline.Elapsed.TotalSeconds >= deadlineSeconds;

            if (outOfTime || budget < chunks.Count)
            {
                // Logged rather than swallowed — a story that routinely exhausts
                // this is a story whose fields are too long, or a backend that is
                // too slow to be useful, and both are worth seeing.
                logger.LogInformation(
                    "FocusAI translation stopped ({Reason}); {Remaining} field(s) left untranslated",
                    outOfTime ? $"{deadlineSeconds}s time budget" : "request budget",
                    segments.Count - i);
                break;
            }

            var pieces = new List<string>(chunks.Count);
            var allAccepted = true;

            foreach (var chunk in chunks)
            {
                budget--;

                var candidate = await CompleteAsync(chunk, cancellationToken);
                var verdict = TranslationGuard.Inspect(chunk, candidate, out var accepted);

                if (verdict != TranslationRejection.None)
                {
                    logger.LogDebug(
                        "FocusAI translation refused ({Reason}) for: {Source}",
                        verdict,
                        chunk.Length <= 120 ? chunk : chunk[..120]);

                    allAccepted = false;
                    break;
                }

                pieces.Add(accepted);
            }

            // All or nothing per field. A field that is half Turkish and half
            // English reads as a bug; the source language throughout reads as a
            // missing feature, which is what it is.
            if (allAccepted && pieces.Count > 0)
            {
                results[i] = string.Join(' ', pieces);
                translated++;
            }
            else
            {
                refused++;
            }
        }

        if (refused > 0)
        {
            logger.LogInformation(
                "FocusAI translated {Translated} of {Total} field(s) from {Language}; {Refused} refused by the guard",
                translated,
                segments.Count,
                sourceLanguage,
                refused);
        }

        return results;
    }

    private async Task<string?> CompleteAsync(string text, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, object?>
        {
            ["model"] = _options.Model,
            ["messages"] = new object[]
            {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = text }
            },
            // Translation is not a creative task, and sampling is where the
            // hallucinated product names come from.
            ["temperature"] = 0d,
            // One token per source character. Text runs roughly four characters to
            // the token, so this is about four times what the translation needs —
            // deliberate headroom, because Turkish is longer than English and a
            // completion cut off mid-sentence comes back to the guard as a length
            // failure and gets thrown away.
            ["max_tokens"] = Math.Clamp(text.Length, 128, 2048),
            ["stream"] = false
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "chat/completions", payload, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "FocusAI translation backend returned {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    body.Length <= 300 ? body : body[..300]);

                return null;
            }

            var parsed = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
                JsonOptions, cancellationToken);

            return parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as TaskCanceledException, which
            // derives from OperationCanceledException — so the usual
            // "when (ex is not OperationCanceledException)" filter lets a timeout
            // through and aborts the entire enrichment batch. The caller's token is
            // the only reliable way to tell a real cancellation from a slow model,
            // and a slow model is the expected case here: CPU inference on a 4B
            // model takes tens of seconds per request.
            logger.LogWarning("FocusAI translation timed out after {Seconds}s", _options.TimeoutSeconds);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The translator being down must never fail enrichment: the story
            // publishes in the source language, which is the pre-existing behaviour.
            logger.LogWarning(ex, "FocusAI translation call failed");
            return null;
        }
    }

    private sealed record ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; init; }
    }

    private sealed record Choice
    {
        [JsonPropertyName("message")]
        public ChoiceMessage? Message { get; init; }
    }

    private sealed record ChoiceMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; init; }
    }
}

/// <summary>
/// What runs when no translation backend is configured — the default. Returns its
/// input untouched, which is precisely the behaviour the pipeline had before the
/// translator existed.
/// </summary>
public sealed class DisabledTranslator : IContentTranslator
{
    public bool IsEnabled => false;

    public string Backend => "none";

    public Task<IReadOnlyList<string>> TranslateAsync(
        IReadOnlyList<string> segments,
        string sourceLanguage,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(segments);
}
