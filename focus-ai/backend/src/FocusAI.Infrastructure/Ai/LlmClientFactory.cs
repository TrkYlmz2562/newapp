using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Enums;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Builds the right <see cref="ILlmClient"/> for a provider, wiring the correct
/// base URL and auth header for each. Named HttpClients come from the factory so
/// the resilience handler and connection pooling apply uniformly.
/// </summary>
public sealed class LlmClientFactory(
    IHttpClientFactory httpClientFactory,
    IOptions<LlmOptions> options,
    ILoggerFactory loggerFactory) : ILlmClientFactory
{
    public const string HttpClientName = "focus-ai-llm";

    private static readonly DisabledLlmClient Disabled = new();

    private readonly LlmOptions _options = options.Value;

    public ILlmClient Create() => Create(_options.Provider);

    public ILlmClient Create(LlmProviderKind provider)
    {
        var providerOptions = _options.For(provider);
        if (provider == LlmProviderKind.Disabled || providerOptions is null)
        {
            return Disabled;
        }

        var logger = loggerFactory.CreateLogger($"FocusAI.Llm.{provider}");
        var http = httpClientFactory.CreateClient(HttpClientName);
        http.Timeout = TimeSpan.FromSeconds(Math.Clamp(providerOptions.TimeoutSeconds, 5, 600));

        switch (provider)
        {
            case LlmProviderKind.Anthropic:
                http.BaseAddress = new Uri(Normalize(providerOptions.BaseUrl ?? "https://api.anthropic.com/"));
                http.DefaultRequestHeaders.Add("x-api-key", providerOptions.ApiKey);
                http.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
                return Ready(new AnthropicClient(http, providerOptions, logger));

            case LlmProviderKind.Gemini:
                http.BaseAddress = new Uri(Normalize(
                    providerOptions.BaseUrl ?? "https://generativelanguage.googleapis.com/"));
                return Ready(new GeminiClient(http, providerOptions, logger));

            case LlmProviderKind.OpenAI:
            case LlmProviderKind.OpenRouter:
            case LlmProviderKind.Ollama:
            case LlmProviderKind.VLlm:
            case LlmProviderKind.LmStudio:
                http.BaseAddress = new Uri(Normalize(providerOptions.BaseUrl ?? DefaultBaseUrl(provider)));

                if (!string.IsNullOrWhiteSpace(providerOptions.ApiKey))
                {
                    http.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", providerOptions.ApiKey);
                }

                if (provider == LlmProviderKind.OpenRouter)
                {
                    // OpenRouter uses these for attribution on its dashboards.
                    http.DefaultRequestHeaders.Add("HTTP-Referer", "https://focus-ai.app");
                    http.DefaultRequestHeaders.Add("X-Title", "Focus AI");
                }

                return Ready(new OpenAiCompatibleClient(http, provider, providerOptions, logger));

            default:
                return Disabled;
        }
    }

    /// <summary>A configured-but-unusable provider is treated as absent, not as an error.</summary>
    private static ILlmClient Ready(ILlmClient client) => client.IsEnabled ? client : Disabled;

    private static string DefaultBaseUrl(LlmProviderKind provider) => provider switch
    {
        LlmProviderKind.OpenAI => "https://api.openai.com/v1/",
        LlmProviderKind.OpenRouter => "https://openrouter.ai/api/v1/",
        LlmProviderKind.Ollama => "http://localhost:11434/v1/",
        LlmProviderKind.VLlm => "http://localhost:8000/v1/",
        LlmProviderKind.LmStudio => "http://localhost:1234/v1/",
        _ => "http://localhost/"
    };

    /// <summary>Relative request paths only compose correctly against a trailing slash.</summary>
    private static string Normalize(string baseUrl) => baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/";
}
