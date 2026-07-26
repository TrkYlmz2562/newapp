using AngleSharp;
using AngleSharp.Dom;
using FocusAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Ingestion;

/// <summary>
/// Pulls readable body text out of an article page for feeds that publish only a
/// teaser. A lightweight readability heuristic: find the densest block-level
/// container and take its paragraphs.
/// </summary>
public sealed class HttpContentExtractor(
    IHttpClientFactory httpClientFactory,
    ILogger<HttpContentExtractor> logger) : IContentExtractor
{
    private static readonly string[] ContentSelectors =
    [
        "article", "main", "[role=main]", ".post-content", ".article-content",
        ".entry-content", "#content", ".content"
    ];

    private static readonly string[] NoiseSelectors =
    [
        "script", "style", "nav", "header", "footer", "aside", "form",
        ".sidebar", ".comments", ".related", ".newsletter", ".advertisement"
    ];

    public async Task<string?> ExtractAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpClientFactory.CreateClient(IngestionServiceNames.HttpClient);
            var html = await http.GetStringAsync(url, cancellationToken);

            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

            foreach (var selector in NoiseSelectors)
            {
                foreach (var element in document.QuerySelectorAll(selector).ToList())
                {
                    element.Remove();
                }
            }

            var best = ContentSelectors
                .Select(document.QuerySelector)
                .FirstOrDefault(element => element is not null && ParagraphLength(element) > 400);

            var container = best ?? document.Body;
            if (container is null)
            {
                return null;
            }

            var paragraphs = container
                .QuerySelectorAll("p, li, h2, h3")
                .Select(node => node.TextContent.Trim())
                .Where(text => text.Length > 40)
                .ToList();

            if (paragraphs.Count == 0)
            {
                return null;
            }

            var text = string.Join("\n\n", paragraphs);
            return text.Length > 50_000 ? text[..50_000] : text;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Extraction is opportunistic — the feed excerpt is always the floor.
            logger.LogDebug(ex, "FocusAI content extraction failed for {Url}", url);
            return null;
        }
    }

    private static int ParagraphLength(IElement element) =>
        element.QuerySelectorAll("p").Sum(p => p.TextContent.Length);
}
