using System.Reflection;
using FocusAI.Application.Common.Interfaces;
using Xunit;

namespace FocusAI.UnitTests.Infrastructure;

/// <summary>
/// The no-LLM search parser. It is the path every deployment without an API key
/// runs on, so its behaviour is worth pinning down precisely.
/// </summary>
public class ExtractiveFallbackParseTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    // ExtractiveFallback is internal to the Infrastructure assembly; reflection
    // keeps the production type's visibility honest rather than widening it for
    // the sake of a test.
    private static ParsedSearchQuery Parse(string query)
    {
        var type = typeof(FocusAI.Infrastructure.Ai.ContentAiService).Assembly
            .GetType("FocusAI.Infrastructure.Ai.ExtractiveFallback")!;

        var method = type.GetMethod("ParseQuery", BindingFlags.Public | BindingFlags.Static)!;
        return (ParsedSearchQuery)method.Invoke(null, [query, Now])!;
    }

    [Theory]
    [InlineData("Son bir ayda çıkan AI haberleri", 30)]
    [InlineData("bu hafta neler oldu", 7)]
    [InlineData("bugün ne var", 1)]
    [InlineData("son 3 ayda MCP hakkında neler oldu", 90)]
    [InlineData("this week in kubernetes", 7)]
    public void Relative_time_phrases_become_absolute_bounds(string query, int expectedDays)
    {
        var parsed = Parse(query);

        Assert.NotNull(parsed.From);
        Assert.Equal(Now.AddDays(-expectedDays), parsed.From!.Value);
    }

    [Fact]
    public void Queries_without_a_time_phrase_have_no_lower_bound() =>
        Assert.Null(Parse("Angular breaking change").From);

    [Fact]
    public void Time_phrase_is_stripped_from_the_keyword_text()
    {
        var parsed = Parse("Son bir ayda çıkan tüm AI Agent haberlerini göster");

        // Regression guard: leaving the phrase in the search text made the whole
        // query match literally nothing.
        Assert.DoesNotContain("ayda", parsed.Text);
        Assert.DoesNotContain("haberlerini", parsed.Text);
        Assert.Contains("ai", parsed.Text);
        Assert.Contains("agent", parsed.Text);
    }

    [Fact]
    public void Longest_time_phrase_wins()
    {
        // "son bir ay" must not be swallowed by a shorter "son ay" match.
        Assert.Equal(Now.AddDays(-90), Parse("son 3 ayda çıkanlar").From);
    }

    [Fact]
    public void Official_source_request_is_detected_and_stripped()
    {
        var parsed = Parse("sadece resmi kaynak olan AI haberleri");

        Assert.True(parsed.OfficialSourcesOnly);
        Assert.DoesNotContain("resmi", parsed.Text);
    }

    [Fact]
    public void A_query_made_entirely_of_filler_still_yields_searchable_text()
    {
        var parsed = Parse("bugün çıkan haberleri göster");

        Assert.False(string.IsNullOrWhiteSpace(parsed.Text));
    }

    [Fact]
    public void Parser_reports_that_no_model_was_used() =>
        Assert.False(Parse("anything").ParsedByLlm);
}

/// <summary>
/// The deterministic local embedding used when no provider is configured. It is
/// lexical, not semantic — these tests pin down what it does and does not promise.
/// </summary>
public class HashingEmbeddingServiceTests
{
    private readonly FocusAI.Infrastructure.Ai.HashingEmbeddingService _service = new();

    [Fact]
    public async Task Embedding_width_matches_the_database_column()
    {
        var vector = await _service.EmbedAsync("dotnet 10 released");

        Assert.Equal(FocusAI.Infrastructure.Persistence.EmbeddingConfig.Dimensions, vector.Length);
    }

    [Fact]
    public async Task Same_text_always_produces_the_same_vector()
    {
        var first = await _service.EmbedAsync("Kubernetes 1.35 released");
        var second = await _service.EmbedAsync("Kubernetes 1.35 released");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task Overlapping_text_scores_higher_than_unrelated_text()
    {
        var original = await _service.EmbedAsync("OpenAI releases GPT-6 with better function calling");
        var similar = await _service.EmbedAsync("OpenAI releases GPT-6 with improved function calling");
        var unrelated = await _service.EmbedAsync("Scientists discover a new species of deep sea coral");

        var closeness = FocusAI.Domain.Text.VectorMath.CosineSimilarity(original, similar);
        var distance = FocusAI.Domain.Text.VectorMath.CosineSimilarity(original, unrelated);

        Assert.True(closeness > distance);
    }

    [Fact]
    public async Task Batch_and_single_calls_agree()
    {
        var single = await _service.EmbedAsync("docker compose v3");
        var batch = await _service.EmbedBatchAsync(["docker compose v3"]);

        Assert.Equal(single, batch[0]);
    }

    [Fact]
    public async Task Vectors_are_unit_length()
    {
        var vector = await _service.EmbedAsync("pgvector hnsw index");
        var magnitude = Math.Sqrt(vector.Sum(v => v * (double)v));

        Assert.Equal(1d, magnitude, 4);
    }
}
