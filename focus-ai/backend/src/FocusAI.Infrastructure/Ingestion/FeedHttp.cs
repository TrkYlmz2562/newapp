using System.Net;
using FocusAI.Domain.Entities.Content;

namespace FocusAI.Infrastructure.Ingestion;

internal sealed record ConditionalResponse(
    bool NotModified,
    string? Body,
    string? ETag,
    string? LastModified,
    HttpStatusCode StatusCode);

/// <summary>
/// Conditional GET helper. Sending the stored validators back turns most polls
/// into a 304, which is the difference between politely reading a hundred feeds
/// every twenty minutes and being rate-limited off them.
/// </summary>
internal static class FeedHttp
{
    public static async Task<ConditionalResponse> GetAsync(
        HttpClient httpClient,
        Source source,
        string? url = null,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url ?? source.FeedUrl);

        if (!string.IsNullOrWhiteSpace(source.ETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", source.ETag);
        }

        if (!string.IsNullOrWhiteSpace(source.LastModified))
        {
            request.Headers.TryAddWithoutValidation("If-Modified-Since", source.LastModified);
        }

        using var response = await httpClient.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotModified)
        {
            return new ConditionalResponse(true, null, source.ETag, source.LastModified, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        return new ConditionalResponse(
            false,
            body,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified?.ToString("R"),
            response.StatusCode);
    }
}
