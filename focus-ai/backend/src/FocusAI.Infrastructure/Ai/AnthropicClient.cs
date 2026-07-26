using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Enums;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Anthropic Messages API client. Kept separate from the OpenAI-compatible path
/// because the system prompt is a top-level field and content comes back as a
/// block list rather than a single string.
/// </summary>
public sealed class AnthropicClient(
    HttpClient httpClient,
    LlmProviderOptions options,
    ILogger logger) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LlmProviderKind Provider => LlmProviderKind.Anthropic;

    public string DefaultModel => options.Model;

    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.ApiKey);

    public async Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = request.Model ?? options.Model;
        var stopwatch = Stopwatch.StartNew();

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxTokens,
            ["temperature"] = request.Temperature,
            ["messages"] = request.Messages
                .Select(m => new { role = m.Role == "assistant" ? "assistant" : "user", content = m.Content })
                .ToList()
        };

        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            payload["system"] = request.SystemPrompt;
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "v1/messages", payload, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "FocusAI Anthropic call failed with {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    Trim(body));

                return LlmResponse.Failed(Provider, model, $"HTTP {(int)response.StatusCode}: {Trim(body)}");
            }

            var parsed = await response.Content.ReadFromJsonAsync<MessagesResponse>(
                JsonOptions, cancellationToken);

            var content = string.Concat(
                parsed?.Content?
                    .Where(block => block.Type == "text" && block.Text is not null)
                    .Select(block => block.Text) ?? []);

            if (string.IsNullOrWhiteSpace(content))
            {
                return LlmResponse.Failed(Provider, model, "Model boş yanıt döndürdü.");
            }

            return new LlmResponse
            {
                Content = content,
                Provider = Provider,
                Model = parsed?.Model ?? model,
                PromptTokens = parsed?.Usage?.InputTokens ?? 0,
                CompletionTokens = parsed?.Usage?.OutputTokens ?? 0,
                LatencyMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "FocusAI Anthropic call threw");
            return LlmResponse.Failed(Provider, model, ex.Message);
        }
    }

    private static string Trim(string value) => value.Length <= 500 ? value : value[..500];

    private sealed record MessagesResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("content")]
        public List<ContentBlock>? Content { get; init; }

        [JsonPropertyName("usage")]
        public UsageInfo? Usage { get; init; }
    }

    private sealed record ContentBlock
    {
        [JsonPropertyName("type")]
        public string? Type { get; init; }

        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    private sealed record UsageInfo
    {
        [JsonPropertyName("input_tokens")]
        public int InputTokens { get; init; }

        [JsonPropertyName("output_tokens")]
        public int OutputTokens { get; init; }
    }
}
