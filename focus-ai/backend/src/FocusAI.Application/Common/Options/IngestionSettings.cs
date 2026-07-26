namespace FocusAI.Application.Common.Options;

/// <summary>
/// The slice of ingestion configuration the Application layer needs.
/// </summary>
/// <remarks>
/// Deliberately narrower than Infrastructure's <c>IngestionOptions</c>: handlers
/// have no business knowing about user agents, HTTP timeouts or job schedules.
/// Both bind to the same <c>Ingestion</c> configuration section.
/// </remarks>
public sealed class IngestionSettings
{
    public const string SectionName = "Ingestion";

    /// <summary>
    /// Fetch the article page when the feed only carried a teaser. Costs one HTTP
    /// request per short item, and is what makes summaries about the article
    /// rather than about its headline.
    /// </summary>
    public bool ExtractFullContent { get; set; } = true;

    public int MaxItemsPerFeed { get; set; } = 50;
}
