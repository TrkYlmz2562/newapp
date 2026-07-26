using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Enums;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Ai;

/// <summary>Google Gemini generateContent client.</summary>
public sealed class GeminiClient(
    HttpClient httpClient,
    LlmProviderOptions options,
    ILogger logger) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LlmProviderKind Provider => LlmProviderKind.Gemini;

    public string DefaultModel => options.Model;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = request.Model ?? options.Model;
        var stopwatch = Stopwatch.StartNew();

        var generationConfig = new Dictionary<string, object?>
        {
            ["temperature"] = request.Temperature,
            ["maxOutputTokens"] = request.MaxTokens
        };

        if (request.JsonMode)
        {
            generationConfig["responseMimeType"] = "application/json";
        }

        var payload = new Dictionary<string, object?>
        {
            ["contents"] = request.Messages
                .Select(m => new
                {
                    role = m.Role == "assistant" ? "model" : "user",
                    parts = new[] { new { text = m.Content } }
                })
                .ToList(),
            ["generationConfig"] = generationConfig
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            payload["systemInstruction"] = new { parts = new[] { new { text = request.SystemPrompt } } };
        }

        try
        {
            // Gemini takes the key as a query parameter rather than a header.
            var url = $"v1beta/models/{model}:generateContent?key={options.ApiKey}";
            using var response = await httpClient.PostAsJsonAsync(url, payload, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "FocusAI Gemini call failed with {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    Trim(body));

                return LlmResponse.Failed(Provider, model, $"HTTP {(int)response.StatusCode}: {Trim(body)}");
            }

            var parsed = await response.Content.ReadFromJsonAsync<GenerateContentResponse>(
                JsonOptions, cancellationToken);

            var content = string.Concat(
                parsed?.Candidates?.FirstOrDefault()?.Content?.Parts?.Select(p => p.Text) ?? []);

            if (string.IsNullOrWhiteSpace(content))
            {
                return LlmResponse.Failed(Provider, model, "Model boş yanıt döndürdü.");
            }

            return new LlmResponse
            {
                Content = content,
                Provider = Provider,
                Model = model,
                PromptTokens = parsed?.UsageMetadata?.PromptTokenCount ?? 0,
                CompletionTokens = parsed?.UsageMetadata?.CandidatesTokenCount ?? 0,
                LatencyMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient surfaces its own timeout as TaskCanceledException, which
            // derives from OperationCanceledException — so the filter below lets a
            // timeout escape and abort the whole enrichment batch instead of
            // degrading that one story to the extractive fallback. The caller's
            // token is what separates a real cancellation from a slow model.
            logger.LogWarning("FocusAI Gemini call timed out");
            return LlmResponse.Failed(Provider, model, "Model zaman aşımına uğradı.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "FocusAI Gemini call threw");
            return LlmResponse.Failed(Provider, model, ex.Message);
        }
    }

    private static string Trim(string value) => value.Length <= 500 ? value : value[..500];

    private sealed record GenerateContentResponse
    {
        [JsonPropertyName("candidates")]
        public List<Candidate>? Candidates { get; init; }

        [JsonPropertyName("usageMetadata")]
        public UsageMetadata? UsageMetadata { get; init; }
    }

    private sealed record Candidate
    {
        [JsonPropertyName("content")]
        public CandidateContent? Content { get; init; }
    }

    private sealed record CandidateContent
    {
        [JsonPropertyName("parts")]
        public List<Part>? Parts { get; init; }
    }

    private sealed record Part
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    private sealed record UsageMetadata
    {
        [JsonPropertyName("promptTokenCount")]
        public int PromptTokenCount { get; init; }

        [JsonPropertyName("candidatesTokenCount")]
        public int CandidatesTokenCount { get; init; }
    }
}
