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

    public int IngestCronMinutes { get; set; } = 20;
}
