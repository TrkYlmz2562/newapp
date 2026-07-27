using FocusAI.Domain.Enums;

namespace FocusAI.Infrastructure.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "focus-ai";

    public string Audience { get; set; } = "focus-ai-app";

    /// <summary>Must be at least 32 bytes. Supplied via configuration/secret store, never checked in.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int AccessTokenMinutes { get; set; } = 30;

    public int RefreshTokenDays { get; set; } = 30;
}

/// <summary>
/// PRD section 11: the provider is configuration, not code. Every field can be
/// overridden per provider so a deployment can mix (e.g. Anthropic for analysis,
/// Ollama for bulk summarisation).
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Llm";

    /// <summary>Provider used when a call does not name one.</summary>
    public LlmProviderKind Provider { get; set; } = LlmProviderKind.Disabled;

    public Dictionary<string, LlmProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Hard ceiling on generation cost per story, across summary + analysis.</summary>
    public int MaxTokensPerStory { get; set; } = 3000;

    public LlmProviderOptions? For(LlmProviderKind kind) =>
        Providers.TryGetValue(kind.ToString(), out var options) ? options : null;
}

public sealed class LlmProviderOptions
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Overrides the provider default; required for self-hosted backends.</summary>
    public string? BaseUrl { get; set; }

    public string Model { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 90;
}

/// <summary>
/// Self-hosted machine translation for text the summariser did not write. Off by
/// default: it costs a container and roughly 2 GB of RAM, and the pipeline is
/// fully functional without it — it just publishes source-language sentences on
/// the no-LLM path.
/// </summary>
public sealed class TranslationOptions
{
    public const string SectionName = "Translation";

    public bool Enabled { get; set; }

    /// <summary>
    /// Any OpenAI-compatible chat endpoint. The reference setup is llama.cpp's
    /// server, which speaks that shape and needs no API key.
    /// </summary>
    public string BaseUrl { get; set; } = "http://translator:8080/v1/";

    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// llama.cpp serves whatever model it was started with and ignores this, so it
    /// is a label rather than a selector — the actual model is chosen by the
    /// container's <c>-hf</c> argument. Kept because an OpenAI-compatible backend
    /// that does route on it (vLLM, LM Studio) needs one.
    /// </summary>
    public string Model { get; set; } = "local-translator";

    /// <summary>
    /// Generous by design. A 1.8B model on CPU runs at roughly 11 tokens/second,
    /// so a long paragraph legitimately takes half a minute. This only ever runs
    /// inside the background enrichment job, where that is nobody's latency.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 180;

    /// <summary>
    /// Longest text sent in one request. Small models degrade sharply on long
    /// inputs, so fields are split on sentence boundaries to stay under this.
    /// </summary>
    public int MaxSegmentChars { get; set; } = 700;

    /// <summary>
    /// Ceiling on requests per story, across all its fields. A story that needs
    /// more than this has its remaining fields left in the source language.
    /// </summary>
    public int MaxRequestsPerStory { get; set; } = 24;

    /// <summary>
    /// Wall-clock ceiling per story, which is the bound that actually matters.
    /// </summary>
    /// <remarks>
    /// A request count alone does not stop one article stalling the batch: 24
    /// requests at the per-request timeout is over an hour, and enrichment runs
    /// stories sequentially inside a job the scheduler starts every 20 minutes.
    /// Two minutes per story keeps a 25-story batch inside its window even when
    /// every call is slow.
    /// </remarks>
    public int MaxSecondsPerStory { get; set; } = 120;
}

public sealed class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>OpenAI-compatible endpoints and Ollama both work here.</summary>
    public LlmProviderKind Provider { get; set; } = LlmProviderKind.Disabled;

    public string Model { get; set; } = "text-embedding-3-small";

    public string ApiKey { get; set; } = string.Empty;

    public string? BaseUrl { get; set; }

    public int BatchSize { get; set; } = 64;
}

public sealed class SearchOptions
{
    public const string SectionName = "Search";

    /// <summary>Meilisearch host. Empty disables the index and falls back to the database.</summary>
    public string Url { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;

    public string IndexName { get; set; } = "stories";
}

public sealed class IngestionOptions
{
    public const string SectionName = "Ingestion";

    public int MaxItemsPerFeed { get; set; } = 50;

    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Identifies the crawler to upstream servers, as good citizenship requires.
    /// Must stay ASCII — HTTP header values are ASCII-only and HttpClient throws
    /// on anything else.
    /// </summary>
    public string UserAgent { get; set; } =
        "FocusAI/1.0 (+https://github.com/TrkYlmz2562/newapp; tech digest platform)";

    /// <summary>Fetch the full article body, not just the feed excerpt.</summary>
    public bool ExtractFullContent { get; set; } = true;

    /// <summary>Run ingestion, clustering and enrichment on a schedule.</summary>
    public bool EnableBackgroundJobs { get; set; } = true;

    /// <summary>
    /// How often the whole pipeline runs.
    /// </summary>
    /// <remarks>
    /// An hour rather than twenty minutes. Three passes an hour did not make the
    /// feed three times fresher — the feeds themselves do not update that often —
    /// but it did triple the churn: partially-clustered stories re-enriched as each
    /// follow-up landed, and failed stories retried twice as often as they needed
    /// to. A story that breaks now is on screen within the hour either way.
    /// </remarks>
    public int IngestCronMinutes { get; set; } = 60;
}
