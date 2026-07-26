using System.Net;
using System.Text;
using System.Text.Json;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Infrastructure.Ai;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace FocusAI.UnitTests.Infrastructure;

public class LocalTranslatorTests
{
    /// <summary>Answers /chat/completions from a queue, or fails in a chosen way.</summary>
    private sealed class StubHandler(Func<string, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastUri = request.RequestUri;

            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            // The user message is the text under translation.
            using var document = JsonDocument.Parse(body);
            var prompt = document.RootElement
                .GetProperty("messages")
                .EnumerateArray()
                .Last()
                .GetProperty("content")
                .GetString() ?? string.Empty;

            return respond(prompt);
        }
    }

    private static HttpResponseMessage Reply(string content)
    {
        var payload = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content } } }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
    }

    private static LocalTranslator Build(
        StubHandler handler,
        Action<TranslationOptions>? configure = null)
    {
        var options = new TranslationOptions
        {
            Enabled = true,
            BaseUrl = "http://translator.test/v1/"
        };

        configure?.Invoke(options);

        var http = new HttpClient(handler) { BaseAddress = new Uri(options.BaseUrl) };

        return new LocalTranslator(http, Options.Create(options), NullLogger<LocalTranslator>.Instance);
    }

    [Fact]
    public async Task A_good_translation_is_used()
    {
        var handler = new StubHandler(_ => Reply("OpenSSH 10.2 kritik bir hatayı düzeltiyor."));
        var translator = Build(handler);

        var result = await translator.TranslateAsync(["OpenSSH 10.2 fixes a critical bug."], "en");

        Assert.Equal(["OpenSSH 10.2 kritik bir hatayı düzeltiyor."], result);

        // The relative path only composes correctly against a trailing slash on the
        // base URL; getting this wrong yields a 404 that looks like a dead backend.
        Assert.Equal("http://translator.test/v1/chat/completions", handler.LastUri?.ToString());
    }

    [Fact]
    public async Task A_translation_that_loses_a_product_name_is_discarded()
    {
        // The measured LibreTranslate output. Publishing it would tell the reader
        // something the source never said, so the English source is kept instead.
        const string source = "React 20 ships a new compiler.";
        var handler = new StubHandler(_ => Reply("20 gemi yeni bir derleyici."));

        var result = await Build(handler).TranslateAsync([source], "en");

        Assert.Equal([source], result);
    }

    [Fact]
    public async Task A_backend_that_is_down_leaves_the_text_alone()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        var result = await Build(handler).TranslateAsync(["Some English text here."], "en");

        // Enrichment must not fail because a side-car container is not running.
        Assert.Equal(["Some English text here."], result);
    }

    [Fact]
    public async Task A_backend_error_response_leaves_the_text_alone()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("model is still loading")
        });

        var result = await Build(handler).TranslateAsync(["Some English text here."], "en");

        Assert.Equal(["Some English text here."], result);
    }

    [Fact]
    public async Task Disabled_translator_makes_no_calls()
    {
        var handler = new StubHandler(_ => Reply("bu çağrılmamalıydı"));
        var translator = Build(handler, options => options.Enabled = false);

        Assert.False(translator.IsEnabled);

        var result = await translator.TranslateAsync(["English text."], "en");

        Assert.Equal(["English text."], result);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task The_result_always_has_one_entry_per_input()
    {
        // The pipeline indexes into this by position, so a short result would
        // silently write one field's text into another.
        var handler = new StubHandler(_ => Reply(string.Empty));

        var segments = new[] { "First English sentence.", "", "Third English sentence." };
        var result = await Build(handler).TranslateAsync(segments, "en");

        Assert.Equal(segments.Length, result.Count);
        Assert.Equal(segments, result);
    }

    [Fact]
    public async Task A_long_field_is_chunked_and_rejoined()
    {
        var source = string.Join(' ', Enumerable.Range(1, 6)
            .Select(i => $"this is a plain sentence numbered {i} and it runs on for a while."));

        // Echo a valid Turkish-ish rendering that keeps the numbers, so the guard
        // accepts every chunk.
        var handler = new StubHandler(prompt => Reply(prompt.Replace("this is a plain sentence numbered", "Bu")));

        var result = await Build(handler, o => o.MaxSegmentChars = 150).TranslateAsync([source], "en");

        Assert.True(handler.Calls > 1, "a long field should be split across several requests");
        Assert.NotEqual(source, result[0]);
        Assert.StartsWith("Bu 1", result[0]);
        Assert.Contains("Bu 6", result[0]);
    }

    [Fact]
    public async Task One_bad_chunk_discards_the_whole_field()
    {
        var source = string.Join(' ', Enumerable.Range(1, 6)
            .Select(i => $"this is a plain sentence numbered {i} and it runs on for a while."));

        var call = 0;
        var handler = new StubHandler(prompt =>
        {
            call++;
            // Second request comes back empty; the guard refuses it.
            return call == 2
                ? Reply(string.Empty)
                : Reply(prompt.Replace("this is a plain sentence numbered", "Bu"));
        });

        var result = await Build(handler, o => o.MaxSegmentChars = 150).TranslateAsync([source], "en");

        // Half Turkish and half English reads as a bug. The source language
        // throughout reads as a missing feature, which is what it is.
        Assert.Equal(source, result[0]);
    }

    [Fact]
    public async Task The_per_story_request_budget_is_respected()
    {
        var handler = new StubHandler(prompt => Reply($"Türkçe: {prompt}"));

        var segments = Enumerable.Range(1, 10)
            .Select(i => $"English sentence {i}.")
            .ToArray();

        var result = await Build(handler, o => o.MaxRequestsPerStory = 3).TranslateAsync(segments, "en");

        Assert.Equal(3, handler.Calls);
        Assert.Equal(segments.Length, result.Count);

        // Whatever the budget did not reach is returned untouched, not dropped.
        Assert.Equal(segments[9], result[9]);
    }

    [Fact]
    public async Task Text_that_is_already_turkish_is_not_sent_at_all()
    {
        // A source registered with the wrong language leaves every one of its
        // stored articles mislabelled, and fixing the source row does not fix them.
        // Reading the text is what stops those being translated into themselves.
        const string source = "Merkez Bankası faiz kararını açıkladı ve piyasalar buna göre tepki verdi.";
        var handler = new StubHandler(_ => Reply("bu çağrılmamalıydı"));

        var result = await Build(handler).TranslateAsync([source], "en");

        Assert.Equal([source], result);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public void Backend_name_reports_whether_translation_is_on()
    {
        var handler = new StubHandler(_ => Reply("x"));

        Assert.Equal("none", Build(handler, o => o.Enabled = false).Backend);
        Assert.StartsWith("local:", Build(handler).Backend);
    }
}
