namespace FocusAI.Domain.Scoring;

/// <summary>What is wrong with a source, if anything.</summary>
public enum SourceHealthStatus
{
    /// <summary>Polling works and content keeps arriving.</summary>
    Healthy,

    /// <summary>Polling works, but nothing new has arrived in a long time for this source.</summary>
    Stale,

    /// <summary>Polling is failing, or has not run in far longer than it should have.</summary>
    Failing,

    /// <summary>Switched off by an operator. Not a fault.</summary>
    Disabled,

    /// <summary>Never successfully polled yet — too early to judge.</summary>
    Unknown
}

/// <summary>Everything the evaluator needs about one source. No entity types, so it stays testable.</summary>
public sealed record SourceHealthInput
{
    public required bool IsEnabled { get; init; }

    public required int FetchIntervalMinutes { get; init; }

    public DateTimeOffset? LastFetchedAt { get; init; }

    public DateTimeOffset? LastSucceededAt { get; init; }

    public required int ConsecutiveFailures { get; init; }

    public string? LastError { get; init; }

    /// <summary>Publication time of the newest article this source has produced.</summary>
    public DateTimeOffset? NewestArticleAt { get; init; }

    /// <summary>
    /// Publication times of this source's recent articles, newest first. Used to
    /// learn the source's own posting rhythm rather than assuming one.
    /// </summary>
    public IReadOnlyList<DateTimeOffset> RecentArticleTimes { get; init; } = [];

    public required DateTimeOffset Now { get; init; }
}

public sealed record SourceHealthResult(
    SourceHealthStatus Status,
    string Reason,
    /// <summary>The source's own median gap between articles, in hours. Null when unknown.</summary>
    double? TypicalGapHours,
    /// <summary>How long since the newest article, in hours. Null when the source has none.</summary>
    double? SilentForHours);

/// <summary>
/// Decides whether a source is actually working, using the source's own history
/// rather than one threshold for everything.
/// </summary>
/// <remarks>
/// The failure this exists for is the one that does not look like a failure. A feed
/// can return HTTP 200 with a valid, well-formed document whose newest item is
/// eighteen months old — the crawler records a success, the source looks green, and
/// nothing new ever arrives. That is worse than a 404, which at least announces
/// itself. Two of the seeded finance feeds behaved exactly this way.
///
/// The hard part is judging "too quiet" without libelling the quiet sources. A
/// vendor blog that posts twice a year is not broken at 60 days; a news wire that
/// posts hourly is broken at 12. So the threshold is derived per source, from the
/// median gap between its own recent articles, and the verdict is deliberately
/// conservative: a source is only called stale when it has gone quiet for several
/// multiples of its own established rhythm, and never on the strength of one or two
/// data points.
/// </remarks>
public static class SourceHealthEvaluator
{
    /// <summary>Consecutive failures before the source is called broken rather than unlucky.</summary>
    private const int FailureTolerance = 3;

    /// <summary>
    /// How many fetch intervals may pass with no successful poll before the source
    /// counts as failing. Generous, because the scheduler itself backs off
    /// exponentially on failure — up to 24× — so a shorter window would flag
    /// sources the scheduler is deliberately resting.
    /// </summary>
    private const int MissedIntervalTolerance = 30;

    /// <summary>
    /// Multiple of a source's own median gap before silence counts as staleness.
    /// Four, so ordinary variance — a quiet week at a weekly blog — does not trip it.
    /// </summary>
    private const double StaleGapMultiple = 4d;

    /// <summary>
    /// Fewest articles needed to claim a rhythm. Two articles give one gap, which
    /// is a coincidence rather than a pattern.
    /// </summary>
    private const int MinSamplesForCadence = 5;

    /// <summary>
    /// Floor on the derived threshold. Without it, a source that happened to publish
    /// three items in one minute would be called stale an hour later.
    /// </summary>
    private const double MinStaleThresholdHours = 72d;

    /// <summary>
    /// Ceiling on the derived threshold, and the absolute limit for sources whose
    /// rhythm cannot be measured. Nothing that has published nothing in three
    /// months is pulling its weight in a daily tech digest.
    /// </summary>
    private const double MaxStaleThresholdHours = 24 * 90d;

    public static SourceHealthResult Evaluate(SourceHealthInput input)
    {
        var typicalGapHours = MedianGapHours(input.RecentArticleTimes);

        var silentForHours = input.NewestArticleAt is { } newest
            ? Math.Max(0d, (input.Now - newest).TotalHours)
            : (double?)null;

        if (!input.IsEnabled)
        {
            return new SourceHealthResult(
                SourceHealthStatus.Disabled,
                "Kapalı. Bu kaynak taranmıyor.",
                typicalGapHours,
                silentForHours);
        }

        if (input.ConsecutiveFailures >= FailureTolerance)
        {
            var detail = string.IsNullOrWhiteSpace(input.LastError)
                ? "Sebep kaydedilmemiş."
                : Shorten(input.LastError);

            return new SourceHealthResult(
                SourceHealthStatus.Failing,
                $"Üst üste {input.ConsecutiveFailures} kez alınamadı. {detail}",
                typicalGapHours,
                silentForHours);
        }

        if (input.LastSucceededAt is not { } lastSuccess)
        {
            // Never fetched at all is different from fetched-and-empty: the first is
            // a new source waiting its turn, the second is a problem.
            return new SourceHealthResult(
                input.LastFetchedAt is null
                    ? SourceHealthStatus.Unknown
                    : SourceHealthStatus.Failing,
                input.LastFetchedAt is null
                    ? "Henüz taranmadı."
                    : "Hiç başarılı tarama yapılamadı.",
                typicalGapHours,
                silentForHours);
        }

        // The scheduler backs off on failure, so a long gap between polls is expected
        // after trouble; only a gap far beyond that means polling has stopped.
        var allowedSilenceHours =
            Math.Max(1, input.FetchIntervalMinutes) * MissedIntervalTolerance / 60d;

        var sinceSuccessHours = (input.Now - lastSuccess).TotalHours;
        if (sinceSuccessHours > allowedSilenceHours)
        {
            return new SourceHealthResult(
                SourceHealthStatus.Failing,
                $"Son başarılı tarama {Days(sinceSuccessHours)} önce. Tarama durmuş görünüyor.",
                typicalGapHours,
                silentForHours);
        }

        if (silentForHours is not { } silent)
        {
            return new SourceHealthResult(
                SourceHealthStatus.Stale,
                "Tarama başarılı ama bu kaynaktan hiç içerik gelmedi.",
                typicalGapHours,
                null);
        }

        var threshold = StaleThresholdHours(typicalGapHours);

        if (silent > threshold)
        {
            // This is the case worth naming precisely: the fetch is fine, so every
            // other signal says the source is healthy.
            var rhythm = typicalGapHours is { } gap
                ? $"Normalde ortalama {Days(gap)} arayla yayın yapıyor."
                : "Yayın aralığı ölçülemedi.";

            return new SourceHealthResult(
                SourceHealthStatus.Stale,
                $"Tarama başarılı ama en yeni içerik {Days(silent)} önceye ait. {rhythm}",
                typicalGapHours,
                silent);
        }

        return new SourceHealthResult(
            SourceHealthStatus.Healthy,
            $"Çalışıyor. En yeni içerik {Days(silent)} önce geldi.",
            typicalGapHours,
            silent);
    }

    /// <summary>
    /// How long this source may stay quiet before it counts as stale, derived from
    /// its own rhythm and clamped so neither a burst nor a lull can produce an
    /// absurd threshold.
    /// </summary>
    public static double StaleThresholdHours(double? typicalGapHours) =>
        typicalGapHours is { } gap
            ? Math.Clamp(gap * StaleGapMultiple, MinStaleThresholdHours, MaxStaleThresholdHours)
            : MaxStaleThresholdHours;

    /// <summary>
    /// Median rather than mean gap between consecutive articles. A source that
    /// publishes a conference's worth of posts in one afternoon and then nothing for
    /// a month has a mean that describes neither behaviour; the median describes the
    /// ordinary case, which is what the threshold is about.
    /// </summary>
    public static double? MedianGapHours(IReadOnlyList<DateTimeOffset> times)
    {
        if (times.Count < MinSamplesForCadence)
        {
            return null;
        }

        var ordered = times.OrderByDescending(t => t).ToList();
        var gaps = new List<double>(ordered.Count - 1);

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var gap = (ordered[i] - ordered[i + 1]).TotalHours;

            // Simultaneous items are a batch import, not a publishing rhythm.
            if (gap > 0)
            {
                gaps.Add(gap);
            }
        }

        if (gaps.Count == 0)
        {
            return null;
        }

        gaps.Sort();
        var middle = gaps.Count / 2;

        return gaps.Count % 2 == 1
            ? gaps[middle]
            : (gaps[middle - 1] + gaps[middle]) / 2d;
    }

    /// <summary>Turkish duration phrase. Hours below a day, days below three months, then months.</summary>
    private static string Days(double hours)
    {
        if (hours < 1)
        {
            return "birkaç dakika";
        }

        if (hours < 48)
        {
            return $"{(int)Math.Round(hours)} saat";
        }

        var days = hours / 24d;
        if (days < 90)
        {
            return $"{(int)Math.Round(days)} gün";
        }

        return $"{(int)Math.Round(days / 30d)} ay";
    }

    private static string Shorten(string error) =>
        error.Length <= 120 ? error : error[..120] + "…";
}
