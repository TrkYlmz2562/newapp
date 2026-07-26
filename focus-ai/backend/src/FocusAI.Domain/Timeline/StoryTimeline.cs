namespace FocusAI.Domain.Timeline;

/// <summary>Where a story is in its own life, judged from when coverage arrived.</summary>
public enum StoryPhase
{
    /// <summary>Broke within the last few hours. Expect the picture to change.</summary>
    Breaking,

    /// <summary>Still collecting coverage, but no longer new.</summary>
    Developing,

    /// <summary>Nothing new for long enough that this is the final shape.</summary>
    Settled
}

/// <summary>A moment worth naming in a story's history.</summary>
public enum TimelineMomentKind
{
    /// <summary>The first outlet to carry it.</summary>
    FirstReport,

    /// <summary>
    /// The first official source to carry it, when that was not the first report.
    /// This is the moment a claim stopped being a claim.
    /// </summary>
    OfficialConfirmation,

    /// <summary>Coverage resumed after a long silence.</summary>
    Resurgence,

    /// <summary>The most recent outlet to carry it.</summary>
    LatestReport
}

/// <summary>One member article, reduced to what the timeline needs.</summary>
public sealed record TimelineEntry(DateTimeOffset At, string SourceName, bool IsOfficial);

public sealed record TimelineMoment(TimelineMomentKind Kind, DateTimeOffset At, string SourceName);

public sealed record StoryTimelineResult(
    StoryPhase Phase,
    DateTimeOffset FirstAt,
    DateTimeOffset LatestAt,
    int OutletCount,
    /// <summary>Hours from the first report to the most recent one.</summary>
    double SpanHours,
    /// <summary>Longest silence between two consecutive reports, in hours.</summary>
    double LongestQuietHours,
    IReadOnlyList<TimelineMoment> Moments);

/// <summary>
/// Derives the shape of a story over time from when each outlet carried it.
/// </summary>
/// <remarks>
/// This answers a question the rest of the detail page does not: is this settled,
/// or is it still moving? A reader deciding whether to act on something needs that
/// more than they need another list of sources — and the coverage comparison next
/// to it deliberately answers a different question, what the outlets said rather
/// than when they said it.
///
/// Everything here is derived from stored timestamps. No model is involved, so it
/// is free, deterministic, and identical on an install with no API keys.
///
/// The moments are deliberately few. A row per article would just be the source
/// list again in a different order; what earns a line is a change in what the
/// story is — it broke, an official body confirmed it, it came back after going
/// quiet, this is where it stands now.
/// </remarks>
public static class StoryTimeline
{
    /// <summary>Under this age, a story is still breaking however much coverage it has.</summary>
    private const double BreakingWithinHours = 12d;

    /// <summary>No new coverage for this long and the story has stopped moving.</summary>
    private const double SettledAfterHours = 48d;

    /// <summary>A silence at least this long is a gap in the story, not a pause.</summary>
    private const double QuietGapHours = 24d;

    public static StoryTimelineResult? Build(IReadOnlyList<TimelineEntry> entries, DateTimeOffset now)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        var ordered = entries.OrderBy(e => e.At).ToList();
        var first = ordered[0];
        var latest = ordered[^1];

        // Outlets, not articles: one outlet filing three updates is one voice, and
        // counting articles would overstate how widely the story is carried.
        var outletCount = ordered
            .Select(e => e.SourceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var spanHours = Math.Max(0d, (latest.At - first.At).TotalHours);
        var sinceFirstHours = Math.Max(0d, (now - first.At).TotalHours);
        var sinceLatestHours = Math.Max(0d, (now - latest.At).TotalHours);

        var phase = sinceLatestHours > SettledAfterHours
            ? StoryPhase.Settled
            : sinceFirstHours <= BreakingWithinHours
                ? StoryPhase.Breaking
                : StoryPhase.Developing;

        var moments = new List<TimelineMoment>
        {
            new(TimelineMomentKind.FirstReport, first.At, first.SourceName)
        };

        // Only when it came later. An official source breaking its own news is the
        // first report, and calling that a "confirmation" would invent a second
        // event out of one.
        var officialConfirmation = ordered.FirstOrDefault(e => e.IsOfficial);
        if (officialConfirmation is not null && officialConfirmation.At > first.At)
        {
            moments.Add(new TimelineMoment(
                TimelineMomentKind.OfficialConfirmation,
                officialConfirmation.At,
                officialConfirmation.SourceName));
        }

        var (longestQuietHours, resumedAt) = LongestGap(ordered);

        // Three reports minimum. With two, the gap between them is the entire span:
        // there is no run of coverage for it to interrupt, so "it went quiet and
        // came back" would be a story about one report and one other report — which
        // first-and-latest already tells, better.
        if (resumedAt is not null && longestQuietHours >= QuietGapHours && ordered.Count >= 3)
        {
            moments.Add(new TimelineMoment(
                TimelineMomentKind.Resurgence,
                resumedAt.At,
                resumedAt.SourceName));
        }

        // Suppressed when it would restate a moment already listed — a two-article
        // story would otherwise show "first report" and "latest report" as two
        // lines describing the same two facts.
        if (latest.At > first.At && moments.All(m => m.At != latest.At))
        {
            moments.Add(new TimelineMoment(TimelineMomentKind.LatestReport, latest.At, latest.SourceName));
        }

        return new StoryTimelineResult(
            phase,
            first.At,
            latest.At,
            outletCount,
            Math.Round(spanHours, 2),
            Math.Round(longestQuietHours, 2),
            moments.OrderBy(m => m.At).ToList());
    }

    /// <summary>
    /// The longest silence between consecutive reports, and the report that ended
    /// it. Returns zero and null when there is nothing to measure.
    /// </summary>
    private static (double Hours, TimelineEntry? ResumedAt) LongestGap(IReadOnlyList<TimelineEntry> ordered)
    {
        var longest = 0d;
        TimelineEntry? resumed = null;

        for (var i = 1; i < ordered.Count; i++)
        {
            var gap = (ordered[i].At - ordered[i - 1].At).TotalHours;
            if (gap > longest)
            {
                longest = gap;
                resumed = ordered[i];
            }
        }

        return (longest, resumed);
    }
}
