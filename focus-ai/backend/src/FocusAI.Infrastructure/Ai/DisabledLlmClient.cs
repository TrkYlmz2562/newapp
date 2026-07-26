using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Enums;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Stand-in used when no provider is configured.
/// </summary>
/// <remarks>
/// It reports failure rather than inventing text. <see cref="ContentAiService"/>
/// detects that and falls back to deterministic extractive summaries, so a
/// developer can run the whole pipeline — ingest, cluster, score, publish — with
/// no API key and still get a populated, honest feed.
/// </remarks>
public sealed class DisabledLlmClient : ILlmClient
{
    public LlmProviderKind Provider => LlmProviderKind.Disabled;

    public string DefaultModel => "none";

    public bool IsEnabled => false;

    public Task<LlmResponse> CompleteAsync(
        LlmRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(LlmResponse.Failed(Provider, DefaultModel, "Yapılandırılmış bir LLM sağlayıcısı yok."));
}
