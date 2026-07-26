using System.Net;
using System.Text;
using FocusAI.Infrastructure.Ingestion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace FocusAI.UnitTests.Infrastructure;

/// <summary>
/// The extractor harvests a lead image from the same page fetch it does for body
/// text — free, and the difference between a blank card and a Twitter-style one.
/// </summary>
public class HttpContentExtractorTests
{
    private const string PageUrl = "https://news.example.com/tech/big-story";

    private static readonly string LongBody = string.Join("\n", new[]
    {
        "<p>Bu makale yeni bir teknolojiyi ayrıntılı biçimde ele alıyor ve konunun neden önemli olduğunu uzun uzun açıklıyor.</p>",
        "<p>İkinci paragraf da bağlam veriyor; kimlerin etkilendiğini ve sektöre olası yansımalarını yeterince uzun anlatıyor.</p>",
        "<p>Üçüncü paragraf sonuçları özetliyor ve okura ne yapması gerektiğine dair somut bir çerçeve sunacak kadar dolu.</p>"
    });

    private static HttpContentExtractor ForHtml(string html)
    {
        var handler = new StubHandler(html);
        var factory = new StubHttpClientFactory(new HttpClient(handler));
        return new HttpContentExtractor(factory, NullLogger<HttpContentExtractor>.Instance);
    }

    [Fact]
    public async Task Reads_og_image_and_keeps_its_query_string()
    {
        var html = $$"""
            <html><head>
              <meta property="og:image" content="https://cdn.example.com/hero.jpg?w=1200&sig=abc">
            </head><body><article>{{LongBody}}</article></body></html>
            """;

        var result = await ForHtml(html).ExtractAsync(PageUrl);

        // Query string is preserved: image CDNs sign and size themselves through it.
        Assert.Equal("https://cdn.example.com/hero.jpg?w=1200&sig=abc", result.ImageUrl);
        Assert.Contains("Bu makale", result.Text);
    }

    [Fact]
    public async Task Resolves_a_relative_og_image_against_the_page_url()
    {
        var html = $$"""
            <html><head>
              <meta property="og:image" content="/media/hero.png">
            </head><body><article>{{LongBody}}</article></body></html>
            """;

        var result = await ForHtml(html).ExtractAsync(PageUrl);

        Assert.Equal("https://news.example.com/media/hero.png", result.ImageUrl);
    }

    [Fact]
    public async Task Falls_back_to_twitter_image_when_no_og_image()
    {
        var html = $$"""
            <html><head>
              <meta name="twitter:image" content="https://cdn.example.com/twitter-card.jpg">
            </head><body><article>{{LongBody}}</article></body></html>
            """;

        var result = await ForHtml(html).ExtractAsync(PageUrl);

        Assert.Equal("https://cdn.example.com/twitter-card.jpg", result.ImageUrl);
    }

    [Fact]
    public async Task Uses_a_real_inline_image_but_skips_logos_and_svgs()
    {
        var html = $$"""
            <html><head></head><body><article>
              <img src="/assets/site-logo.svg" alt="logo">
              <img src="data:image/gif;base64,R0lGODlh" alt="pixel">
              <img src="https://cdn.example.com/in-body-photo.jpg" alt="photo">
              {{LongBody}}
            </article></body></html>
            """;

        var result = await ForHtml(html).ExtractAsync(PageUrl);

        Assert.Equal("https://cdn.example.com/in-body-photo.jpg", result.ImageUrl);
    }

    [Fact]
    public async Task Returns_body_but_no_image_when_the_page_has_none()
    {
        var html = $$"""
            <html><head></head><body><article>{{LongBody}}</article></body></html>
            """;

        var result = await ForHtml(html).ExtractAsync(PageUrl);

        Assert.Null(result.ImageUrl);
        Assert.Contains("Üçüncü paragraf", result.Text);
    }

    private sealed class StubHandler(string html) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            });
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
