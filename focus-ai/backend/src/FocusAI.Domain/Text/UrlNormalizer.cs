namespace FocusAI.Domain.Text;

/// <summary>
/// Strips the tracking cruft that makes the same article look like five
/// different URLs. Running before hashing is what lets exact-duplicate detection
/// work at all across aggregators.
/// </summary>
public static class UrlNormalizer
{
    private static readonly string[] TrackingPrefixes = ["utm_", "mc_", "pk_", "hsa_", "vero_"];

    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "ref", "referrer", "source", "fbclid", "gclid", "igshid", "spm", "cmpid",
        "_hsenc", "_hsmi", "mkt_tok", "sh", "share", "at_medium", "at_campaign"
    };

    public static string Normalize(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return url.Trim();
        }

        var scheme = uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) ? "https" : uri.Scheme;
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        host = host.ToLowerInvariant();

        var path = uri.AbsolutePath;
        if (path.Length > 1 && path.EndsWith('/'))
        {
            path = path.TrimEnd('/');
        }

        var kept = ParseQuery(uri.Query)
            .Where(pair => !IsTracking(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => string.IsNullOrEmpty(pair.Value) ? pair.Key : $"{pair.Key}={pair.Value}")
            .ToArray();

        var query = kept.Length > 0 ? "?" + string.Join('&', kept) : string.Empty;
        var port = uri.IsDefaultPort ? string.Empty : $":{uri.Port}";

        // The fragment is always dropped: it never identifies a different article.
        return $"{scheme}://{host}{port}{path}{query}";
    }

    private static bool IsTracking(string key) =>
        TrackingParams.Contains(key) ||
        TrackingPrefixes.Any(prefix => key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<KeyValuePair<string, string>> ParseQuery(string query)
    {
        if (string.IsNullOrEmpty(query) || query == "?")
        {
            yield break;
        }

        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var index = part.IndexOf('=');
            yield return index < 0
                ? new KeyValuePair<string, string>(part, string.Empty)
                : new KeyValuePair<string, string>(part[..index], part[(index + 1)..]);
        }
    }
}
