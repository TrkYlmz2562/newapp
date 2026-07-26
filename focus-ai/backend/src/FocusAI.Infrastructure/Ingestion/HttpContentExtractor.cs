using AngleSharp;
using AngleSharp.Dom;
using FocusAI.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;

namespace FocusAI.Infrastructure.Ingestion;

/// <summary>
/// Pulls readable body text — and, from the same page fetch, a lead image — out of
/// an article page for feeds that publish only a teaser. A lightweight readability
/// heuristic: find the densest block-level container and take its paragraphs. The
/// image is a free by-product: the page is already downloaded and parsed, so we
/// read its og:image/twitter:image without any extra request.
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

    // Ordered by trust: social-card metadata first (it is what the publisher chose
    // to represent the article), then a significant inline image as a last resort.
    private static readonly (string Selector, string Attribute)[] ImageMeta =
    [
        ("meta[property='og:image']", "content"),
        ("meta[property='og:image:secure_url']", "content"),
        ("meta[property='og:image:url']", "content"),
        ("meta[name='twitter:image']", "content"),
        ("meta[name='twitter:image:src']", "content"),
        ("link[rel='image_src']", "href")
    ];

    public async Task<ExtractedArticle> ExtractAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpClientFactory.CreateClient(IngestionServiceNames.HttpClient);
            var html = await http.GetStringAsync(url, cancellationToken);

            var context = BrowsingContext.New(AngleSharp.Configuration.Default);
            var document = await context.OpenAsync(req => req.Content(html), cancellationToken);

            // Read the social-card image before stripping noise: the <head> meta tags
            // survive noise removal anyway, but reading first keeps the two concerns
            // independent.
            var image = FindMetaImage(document, url);

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

            // Only look for an inline image once nav/header/footer are gone, so we do
            // not mistake a site logo for the article's lead image.
            image ??= FindInlineImage(container, url);

            if (container is null)
            {
                return new ExtractedArticle(null, image);
            }

            var paragraphs = container
                .QuerySelectorAll("p, li, h2, h3")
                .Select(node => node.TextContent.Trim())
                .Where(text => text.Length > 40)
                .ToList();

            var text = paragraphs.Count == 0
                ? null
                : string.Join("\n\n", paragraphs);

            if (text is { Length: > 50_000 })
            {
                text = text[..50_000];
            }

            return new ExtractedArticle(text, image);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Extraction is opportunistic — the feed excerpt is always the floor.
            logger.LogDebug(ex, "FocusAI content extraction failed for {Url}", url);
            return ExtractedArticle.Empty;
        }
    }

    private static string? FindMetaImage(IDocument document, string pageUrl)
    {
        foreach (var (selector, attribute) in ImageMeta)
        {
            var raw = document.QuerySelector(selector)?.GetAttribute(attribute);
            if (Resolve(raw, pageUrl) is { } resolved)
            {
                return resolved;
            }
        }

        return null;
    }

    private static string? FindInlineImage(IElement? container, string pageUrl)
    {
        var candidates = (container ?? throw new ArgumentNullException(nameof(container)))
            .QuerySelectorAll("img");

        foreach (var img in candidates)
        {
            if (!IsLikelyContentImage(img))
            {
                continue;
            }

            var raw = img.GetAttribute("src") ?? img.GetAttribute("data-src");
            if (Resolve(raw, pageUrl) is { } resolved)
            {
                return resolved;
            }
        }

        return null;
    }

    /// <summary>Rejects icons, sprites, tracking pixels and tiny decorative images.</summary>
    private static bool IsLikelyContentImage(IElement img)
    {
        var src = (img.GetAttribute("src") ?? img.GetAttribute("data-src") ?? string.Empty).ToLowerInvariant();
        if (src.Length == 0 || src.StartsWith("data:") || src.EndsWith(".svg"))
        {
            return false;
        }

        if (src.Contains("logo") || src.Contains("icon") || src.Contains("sprite") ||
            src.Contains("avatar") || src.Contains("pixel") || src.Contains("1x1"))
        {
            return false;
        }

        if (int.TryParse(img.GetAttribute("width"), out var width) && width is > 0 and < 200)
        {
            return false;
        }

        if (int.TryParse(img.GetAttribute("height"), out var height) && height is > 0 and < 120)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Turns a raw src/href (possibly relative or protocol-relative) into an absolute
    /// http(s) URL. Image URLs are deliberately NOT run through UrlNormalizer: it forces
    /// https, reorders and drops query params — fine for article dedup keys, but it can
    /// break CDN images that sign or size themselves via the query string.
    /// </summary>
    private static string? Resolve(string? raw, string pageUrl)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();
        if (raw.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Only accept the raw value as-is when it is already an absolute http(s) URL.
        // A root-relative path like "/media/hero.png" is treated by Uri as an implicit
        // file:// URI on Unix, so we must NOT trust UriKind.Absolute alone — anything
        // that is not http(s) is resolved against the page URL instead.
        Uri? resolved = null;
        if (Uri.TryCreate(raw, UriKind.Absolute, out var abs) && IsHttp(abs))
        {
            resolved = abs;
        }
        else if (Uri.TryCreate(pageUrl, UriKind.Absolute, out var baseUri) &&
                 Uri.TryCreate(baseUri, raw, out var combined))
        {
            resolved = combined;
        }

        if (resolved is null || !IsHttp(resolved))
        {
            return null;
        }

        // The column is varchar(2000); a longer URL is rejected rather than truncated
        // into a broken link — the frontend has a category illustration fallback.
        var absolute = resolved.ToString();
        return absolute.Length <= 2000 ? absolute : null;
    }

    private static bool IsHttp(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private static int ParagraphLength(IElement element) =>
        element.QuerySelectorAll("p").Sum(p => p.TextContent.Length);
}
