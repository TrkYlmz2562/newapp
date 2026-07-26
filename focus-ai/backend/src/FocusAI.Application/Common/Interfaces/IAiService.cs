using FocusAI.Domain.Enums;

namespace FocusAI.Application.Common.Interfaces;

public sealed record LlmMessage(string Role, string Content)
{
    public static LlmMessage User(string content) => new("user", content);

    public static LlmMessage Assistant(string content) => new("assistant", content);
}

public sealed record LlmRequest
{
    public required IReadOnlyList<LlmMessage> Messages { get; init; }

    public string? SystemPrompt { get; init; }

    /// <summary>Low by default: this product summarises facts, it does not write fiction.</summary>
    public double Temperature { get; init; } = 0.2;

    public int MaxTokens { get; init; } = 1200;

    /// <summary>Ask the provider for strict JSON where it supports doing so.</summary>
    public bool JsonMode { get; init; }

    /// <summary>Overrides the configured default model for this one call.</summary>
    public string? Model { get; init; }

    /// <summary>Labels the call in <c>AiUsageLog</c>, e.g. "summary" or "ask".</summary>
    public string Operation { get; init; } = "generic";
}

public sealed record LlmResponse
{
    public required string Content { get; init; }

    public required LlmProviderKind Provider { get; init; }

    public required string Model { get; init; }

    public int PromptTokens { get; init; }

    public int CompletionTokens { get; init; }

    public int LatencyMs { get; init; }

    public bool Succeeded { get; init; } = true;

    public string? Error { get; init; }

    public static LlmResponse Failed(LlmProviderKind provider, string model, string error) =>
        new() { Content = string.Empty, Provider = provider, Model = model, Succeeded = false, Error = error };
}

/// <summary>
/// Provider-agnostic chat port (PRD section 11). One implementation per backend;
/// the factory picks between them from configuration at runtime.
/// </summary>
public interface ILlmClient
{
    LlmProviderKind Provider { get; }

    string DefaultModel { get; }

    /// <summary>False when no credentials are configured — callers fall back gracefully.</summary>
    bool IsEnabled { get; }

    Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken = default);
}

public interface ILlmClientFactory
{
    /// <summary>Resolves the client for the configured default provider.</summary>
    ILlmClient Create();

    ILlmClient Create(LlmProviderKind provider);
}

/// <summary>Text → vector port. Dimensions must stay stable for a given deployment.</summary>
public interface IEmbeddingService
{
    int Dimensions { get; }

    string ModelName { get; }

    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
