using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Text;
using FocusAI.Infrastructure.Configuration;
using FocusAI.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Ai;

/// <summary>
/// Embeddings from any OpenAI-compatible <c>/embeddings</c> endpoint (OpenAI,
/// Ollama, vLLM, LM Studio).
/// </summary>
public sealed class OpenAiEmbeddingService(
    HttpClient httpClient,
    EmbeddingOptions options,
    ILogger<OpenAiEmbeddingService> logger) : IEmbeddingService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public int Dimensions => EmbeddingConfig.Dimensions;

    public string ModelName => options.Model;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await EmbedBatchAsync([text], cancellationToken);
        return result.Count > 0 ? result[0] : [];
    }

    public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts.Count == 0)
        {
            return [];
        }

        var results = new List<float[]>(texts.Count);
        var batchSize = Math.Clamp(options.BatchSize, 1, 256);

        for (var offset = 0; offset < texts.Count; offset += batchSize)
        {
            var batch = texts.Skip(offset).Take(batchSize).ToList();

            var payload = new Dictionary<string, object?>
            {
                ["model"] = options.Model,
                ["input"] = batch
            };

            // OpenAI's v3 models support truncating the output to a fixed size;
            // asking for exactly the column width avoids a dimension mismatch.
            if (options.Model.StartsWith("text-embedding-3", StringComparison.OrdinalIgnoreCase))
            {
                payload["dimensions"] = Dimensions;
            }

            using var response = await httpClient.PostAsJsonAsync(
                "embeddings", payload, JsonOptions, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                logger.LogError(
                    "FocusAI embedding request failed with {StatusCode}: {Body}",
                    (int)response.StatusCode,
                    body.Length > 300 ? body[..300] : body);

                throw new InvalidOperationException($"Embedding isteği başarısız: HTTP {(int)response.StatusCode}");
            }

            var parsed = await response.Content.ReadFromJsonAsync<EmbeddingResponse>(
                JsonOptions, cancellationToken);

            foreach (var item in (parsed?.Data ?? []).OrderBy(d => d.Index))
            {
                var vector = item.Embedding ?? [];

                // A model whose native width differs from the column would break
                // every insert; failing loudly here is far easier to diagnose.
                if (vector.Length != Dimensions)
                {
                    throw new InvalidOperationException(
                        $"Embedding boyutu uyuşmuyor: model {vector.Length}, beklenen {Dimensions}. " +
                        "Embeddings:Model ayarını veya EmbeddingConfig.Dimensions değerini güncelleyin.");
                }

                results.Add(vector);
            }
        }

        return results;
    }

    private sealed record EmbeddingResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingData>? Data { get; init; }
    }

    private sealed record EmbeddingData
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; init; }
    }
}

/// <summary>
/// Deterministic local embeddings — hashed bag-of-shingles projected onto the
/// configured dimension, then L2-normalised.
/// </summary>
/// <remarks>
/// This is not a semantic model and does not pretend to be: it captures lexical
/// overlap only. Its purpose is that <c>docker compose up</c> yields a working
/// clustering and related-stories pipeline with no API key and no cost. Set a
/// real provider before drawing any conclusion about semantic quality.
/// </remarks>
public sealed class HashingEmbeddingService : IEmbeddingService
{
    public int Dimensions => EmbeddingConfig.Dimensions;

    public string ModelName => "local-hashing-v1";

    public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default) =>
        Task.FromResult(Embed(text));

    public Task<IReadOnlyList<float[]>> EmbedBatchAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

    private float[] Embed(string text)
    {
        var vector = new float[Dimensions];

        foreach (var shingle in TextNormalizer.Shingles(text))
        {
            var hash = Fnv1A32(shingle);
            var index = (int)(hash % (uint)Dimensions);

            // A second hash bit decides the sign, which keeps unrelated terms
            // from all pushing the same direction and washing out the signal.
            var sign = (hash & 0x8000_0000u) != 0 ? -1f : 1f;
            vector[index] += sign;
        }

        // Unigrams too, so single-token titles still produce a usable vector.
        foreach (var token in TextNormalizer.Tokenize(text))
        {
            var hash = Fnv1A32(token);
            vector[(int)(hash % (uint)Dimensions)] += (hash & 0x8000_0000u) != 0 ? -0.5f : 0.5f;
        }

        return VectorMath.Normalize(vector);
    }

    private static uint Fnv1A32(string value)
    {
        const uint offsetBasis = 2166136261;
        const uint prime = 16777619;

        var hash = offsetBasis;
        foreach (var b in Encoding.UTF8.GetBytes(value))
        {
            hash ^= b;
            hash *= prime;
        }

        return hash;
    }
}
