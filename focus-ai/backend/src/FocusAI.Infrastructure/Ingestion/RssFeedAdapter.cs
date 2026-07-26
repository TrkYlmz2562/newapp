using System.ServiceModel.Syndication;
using System.Xml;
using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Enums;
using FocusAI.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FocusAI.Infrastructure.Ingestion;

/// <summary>
/// Handles every XML syndication source: RSS 2.0, Atom, YouTube channel feeds,
/// arXiv's Atom export and Product Hunt. That is the bulk of the PRD section 6
/// catalogue — official vendor blogs are almost universally plain RSS.
/// </summary>
public sealed class RssFeedAdapter(
    IHttpClientFactory httpClientFactory,
    IOptions<IngestionOptions> options,
    ILogger<RssFeedAdapter> logger) : IFeedAdapter
{
    private readonly IngestionOptions _options = options.Value;

    private static readonly SourceKind[] Supported =
    [
        SourceKind.Rss,
        SourceKind.Atom,
        SourceKind.YouTubeRss,
        SourceKind.Arxiv,
        SourceKind.ProductHunt,
        SourceKind.PapersWithCode,
        SourceKind.GitHubReleases
    ];

    public bool CanHandle(Source source) => Supported.Contains(source.Kind);

    public async Task<FeedFetchResult> FetchAsync(Source source, CancellationToken cancellationToken = default)
    {
        try
        {
            var http = httpClientFactory.CreateClient(IngestionServiceNames.HttpClient);
            var response = await FeedHttp.GetAsync(http, source, cancellationToken: cancellationToken);

            if (response.NotModified)
            {
                return FeedFetchResult.Empty(notModified: true);
            }

            if (string.IsNullOrWhiteSpace(response.Body))
            {
                return FeedFetchResult.Empty();
            }

            using var reader = XmlReader.Create(
                new StringReader(response.Body),
                new XmlReaderSettings
                {
                    // Feeds are untrusted input: no DTD processing, no external entities.
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                });

            var feed = SyndicationFeed.Load(reader);
            if (feed is null)
            {
                return FeedFetchResult.Failure("Feed ayrıştırılamadı.");
            }

            var items = feed.Items
                .Take(_options.MaxItemsPerFeed)
                .Select(item => MapItem(item, source))
                .Where(item => item is not null)
                .Select(item => item!)
                .ToList();

            return new FeedFetchResult
            {
                Items = items,
                ETag = response.ETag,
                LastModified = response.LastModified
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "FocusAI RSS fetch failed for {SourceSlug}", source.Slug);
            return FeedFetchResult.Failure(ex.Message);
        }
    }

    private static FeedItem? MapItem(SyndicationItem item, Source source)
    {
        var link = item.Links
            .FirstOrDefault(l => l.RelationshipType is null or "alternate")?.Uri?.ToString()
            ?? item.Links.FirstOrDefault()?.Uri?.ToString();

        if (string.IsNullOrWhiteSpace(link) || string.IsNullOrWhiteSpace(item.Title?.Text))
        {
            return null;
        }

        var content = (item.Content as TextSyndicationContent)?.Text;
        var summary = item.Summary?.Text;

        // Atom puts the body in <content>, RSS in <description>; either can be
        // the richer field, so keep whichever has more to say.
        var excerpt = HtmlText.ToPlainText(summary ?? content);
        var body = HtmlText.ToPlainText(content ?? summary);

        var published = item.PublishDate != default
            ? item.PublishDate
            : item.LastUpdatedTime != default
                ? item.LastUpdatedTime
                : (DateTimeOffset?)null;

        return new FeedItem
        {
            // Some feeds omit <guid> entirely; the link is the stable fallback.
            ExternalId = string.IsNullOrWhiteSpace(item.Id) ? link : item.Id,
            Url = link,
            Title = HtmlText.ToPlainText(item.Title.Text) ?? item.Title.Text,
            Author = item.Authors.FirstOrDefault()?.Name,
            Excerpt = Shorten(excerpt, 4000),
            Content = source.Kind == SourceKind.YouTubeRss ? excerpt : Shorten(body, 50_000),
            ImageUrl = FindImage(item),
            PublishedAt = published
        };
    }

    private static string? FindImage(SyndicationItem item)
    {
        var enclosure = item.Links.FirstOrDefault(l =>
            l.RelationshipType == "enclosure" &&
            l.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true);

        if (enclosure?.Uri is not null)
        {
            return enclosure.Uri.ToString();
        }

        // media:thumbnail / media:content, used by YouTube and many news feeds.
        foreach (var extension in item.ElementExtensions)
        {
            if (extension.OuterName is not ("thumbnail" or "content"))
            {
                continue;
            }

            var element = extension.GetObject<System.Xml.Linq.XElement>();
            var url = element.Attribute("url")?.Value;
            if (!string.IsNullOrWhiteSpace(url))
            {
                return url;
            }
        }

        return null;
    }

    private static string? Shorten(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}
