using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Scoring;

public sealed record ImportanceScoreInput
{
    /// <summary>Trust score for the same story, already computed.</summary>
    public required int TrustScore { get; init; }

    public required int DistinctSourceCount { get; init; }

    public required int OfficialSourceCount { get; init; }

    public int EngagementScore { get; init; }

    public required DateTimeOffset PublishedAt { get; init; }

    public required DateTimeOffset Now { get; init; }

    /// <summary>
    /// Editorial significance in [0,1] as judged by the summariser: is this a
    /// capability change developers must react to, or a press release? Null
    /// falls back to a neutral prior so the pipeline still ranks without an LLM.
    /// </summary>
    public double? LlmImportance { get; init; }

    public ContentCategory Category { get; init; } = ContentCategory.Unknown;
}

/// <summary>
/// Global (non-personalised) newsworthiness, used to pick the shared edition and
/// to gate breaking-news pushes. Personalisation is layered on top of this by
/// <see cref="PersonalizationScorer"/>.
/// </summary>
public static class ImportanceScoreCalculator
{
    private const double LlmWeight = 0.35;
    private const double BreadthWeight = 0.20;
    private const double FreshnessWeight = 0.15;
    private const double TrustWeight = 0.15;
    private const double EngagementWeight = 0.15;

    private const double DefaultLlmImportance = 0.5;
    private const int BreadthSaturation = 10;
    private const double EngagementSaturation = 1500d;

    public static int Calculate(ImportanceScoreInput input)
    {
        var llm = Math.Clamp(input.LlmImportance ?? DefaultLlmImportance, 0d, 1d) * 100;
        var breadth = Breadth(input.DistinctSourceCount, input.OfficialSourceCount);
        var freshness = Freshness(input.PublishedAt, input.Now);
        var engagement = Engagement(input.EngagementScore);

        var raw =
            llm * LlmWeight +
            breadth * BreadthWeight +
            freshness * FreshnessWeight +
            input.TrustScore * TrustWeight +
            engagement * EngagementWeight;

        return Clamp(raw * CategoryMultiplier(input.Category));
    }

    /// <summary>
    /// Breadth of pickup, with official sources counted double: fifteen blogs
    /// reprinting a press release is weaker evidence of significance than three
    /// vendors shipping coordinated announcements.
    /// </summary>
    private static double Breadth(int distinctSources, int officialSources)
    {
        var effective = distinctSources + officialSources;
        if (effective <= 0)
        {
            return 0d;
        }

        var ratio = Math.Log2(1 + effective) / Math.Log2(1 + BreadthSaturation);
        return Math.Min(1d, ratio) * 100;
    }

    /// <summary>
    /// Steeper decay than the trust score uses — a 36-hour-old item should not
    /// out-rank this morning's news in a *daily* digest even if it is solid.
    /// </summary>
    private static double Freshness(DateTimeOffset publishedAt, DateTimeOffset now)
    {
        var hours = Math.Max(0d, (now - publishedAt).TotalHours);
        return 100 * Math.Pow(0.5, hours / 24d);
    }

    private static double Engagement(int engagement)
    {
        if (engagement <= 0)
        {
            return 25d;
        }

        var ratio = Math.Log10(1 + engagement) / Math.Log10(1 + EngagementSaturation);
        return Math.Min(1d, ratio) * 100;
    }

    /// <summary>
    /// Slight editorial thumb on the scale toward the categories the product
    /// exists to cover. Kept close to 1.0 so it nudges rather than overrides.
    /// </summary>
    private static double CategoryMultiplier(ContentCategory category) => category switch
    {
        ContentCategory.Ai => 1.08,
        ContentCategory.Software => 1.05,
        ContentCategory.OpenSource => 1.03,
        ContentCategory.Security => 1.05,
        ContentCategory.Tools => 1.0,
        ContentCategory.Startup => 0.95,
        ContentCategory.Career => 0.92,
        ContentCategory.Science => 0.95,
        ContentCategory.Hardware => 0.95,
        ContentCategory.Product => 0.95,
        _ => 0.9
    };

    private static int Clamp(double value) => (int)Math.Round(Math.Clamp(value, 0d, 100d));
}
