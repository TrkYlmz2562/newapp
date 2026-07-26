using FocusAI.Application.Common.Interfaces;
using FocusAI.Domain.Entities.Content;

namespace FocusAI.Infrastructure.Ingestion;

internal static class IngestionServiceNames
{
    public const string HttpClient = "focus-ai-ingestion";
}

/// <summary>
/// Picks the adapter for a source. Registration order decides precedence, and
/// the RSS adapter is registered last so it acts as the catch-all.
/// </summary>
public sealed class FeedAdapterResolver(IEnumerable<IFeedAdapter> adapters) : IFeedAdapterResolver
{
    private readonly IReadOnlyList<IFeedAdapter> _adapters = adapters.ToList();

    public IFeedAdapter Resolve(Source source)
    {
        var adapter = _adapters.FirstOrDefault(a => a.CanHandle(source));

        if (adapter is null)
        {
            throw new NotSupportedException(
                $"'{source.Kind}' türü için bir ingestion adaptörü kayıtlı değil (kaynak: {source.Slug}).");
        }

        return adapter;
    }
}
