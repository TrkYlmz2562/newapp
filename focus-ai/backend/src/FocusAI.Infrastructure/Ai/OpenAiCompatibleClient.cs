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
/// One client for every backend that speaks the OpenAI <c>/chat/completions</c>
/// shape: OpenAI itself, OpenRouter, Ollama, vLLM and LM Studio. That covers
/// five of the seven providers named in PRD section 11 with a single code path.
/// </summary>
public sealed class OpenAiCompatibleClient(
    HttpClient httpClient,
    LlmProviderKind provider,
    LlmProviderOptions options,
    ILogger logger) : ILlmClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public LlmProviderKind Provider => provider;

    public string DefaultModel => options.Model;

    /// <summary>
    /// Self-hosted backends (Ollama, vLLM, LM Studio) legitimately have no API
    /// key, so a configured base URL is enough to consider them usable.
    /// </summary>
    public bool IsEnabled =>
        !string.IsNullOrWhiteSpace(options.ApiKey) || !string.IsNullOrWhiteSpace(options.BaseUrl);

    public async Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default)
    {
        var model = request.Model ?? options.Model;
        var stopwatch = Stopwatch.StartNew();

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }

        messages.AddRange(request.Messages.Select(m => new { role = m.Role, content = m.Content }));

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["temperature"] = request.Temperature,
            ["max_tokens"] = request.MaxTokens
        };

        if (request.JsonMode)
        {
            payload["response_format"] = new { type = "json_object" };
        }

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "chat/completions", payload, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogWarning(
                    "FocusAI LLM call to {Provider} failed with {StatusCode}: {Body}",
                    provider,
                    (int)response.StatusCode,
                    Trim(body));

                return LlmResponse.Failed(provider, model, $"HTTP {(int)response.StatusCode}: {Trim(body)}");
            }

            var parsed = await response.Content.ReadFromJsonAsync<ChatCompletionResponse>(
                JsonOptions, cancellationToken);

            var content = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return LlmResponse.Failed(provider, model, "Model boş yanıt döndürdü.");
            }

            return new LlmResponse
            {
                Content = content,
                Provider = provider,
                Model = parsed?.Model ?? model,
                PromptTokens = parsed?.Usage?.PromptTokens ?? 0,
                CompletionTokens = parsed?.Usage?.CompletionTokens ?? 0,
                LatencyMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "FocusAI LLM call to {Provider} threw", provider);
            return LlmResponse.Failed(provider, model, ex.Message);
        }
    }

    private static string Trim(string value) => value.Length <= 500 ? value : value[..500];

    private sealed record ChatCompletionResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("choices")]
        public List<Choice>? Choices { get; init; }

        [JsonPropertyName("usage")]
        public UsageInfo? Usage { get; init; }
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

    private sealed record UsageInfo
    {
        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; init; }
    }
}
