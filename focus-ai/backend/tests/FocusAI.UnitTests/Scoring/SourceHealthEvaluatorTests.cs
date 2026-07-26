using FocusAI.Domain.Scoring;
using Xunit;

namespace FocusAI.UnitTests.Scoring;

public class SourceHealthEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A source publishing every <paramref name="gapHours"/> hours, newest first.</summary>
    private static List<DateTimeOffset> Cadence(double gapHours, int count, double newestAgeHours = 1) =>
        Enumerable.Range(0, count)
            .Select(i => Now.AddHours(-(newestAgeHours + i * gapHours)))
            .ToList();

    private static SourceHealthInput Input(
        IReadOnlyList<DateTimeOffset>? times = null,
        bool enabled = true,
        int failures = 0,
        int intervalMinutes = 60,
        DateTimeOffset? lastSucceeded = null,
        DateTimeOffset? lastFetched = null,
        string? lastError = null)
    {
        var samples = times ?? Cadence(24, 10);

        return new SourceHealthInput
        {
            IsEnabled = enabled,
            FetchIntervalMinutes = intervalMinutes,
            LastFetchedAt = lastFetched ?? Now.AddMinutes(-10),
            LastSucceededAt = lastSucceeded ?? Now.AddMinutes(-10),
            ConsecutiveFailures = failures,
            LastError = lastError,
            NewestArticleAt = samples.Count > 0 ? samples[0] : null,
            RecentArticleTimes = samples,
            Now = Now
        };
    }

    [Fact]
    public void A_working_source_is_healthy()
    {
        var result = SourceHealthEvaluator.Evaluate(Input());

        Assert.Equal(SourceHealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void A_feed_that_returns_200_but_stopped_publishing_is_stale()
    {
        // The case this whole class exists for. Two of the seeded finance feeds
        // behaved exactly this way: valid document, HTTP 200, newest item 18 months
        // old. Every other signal says the source is fine.
        var times = Cadence(24, 10, newestAgeHours: 24 * 550);

        var result = SourceHealthEvaluator.Evaluate(Input(times));

        Assert.Equal(SourceHealthStatus.Stale, result.Status);
        Assert.Contains("Tarama başarılı", result.Reason);
        Assert.Contains("ay önceye ait", result.Reason);
    }

    [Fact]
    public void A_blog_that_posts_monthly_is_not_stale_after_three_weeks()
    {
        // The mistake worth avoiding: a fixed threshold libels every low-volume
        // vendor blog, and a health page that cries wolf gets ignored.
        var times = Cadence(24 * 30, 10, newestAgeHours: 24 * 21);

        var result = SourceHealthEvaluator.Evaluate(Input(times));

        Assert.Equal(SourceHealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void A_wire_that_posts_hourly_is_stale_after_a_week()
    {
        // Same silence, opposite verdict — because the source's own rhythm differs
        // by three orders of magnitude from the monthly blog above.
        var times = Cadence(1, 20, newestAgeHours: 24 * 7);

        var result = SourceHealthEvaluator.Evaluate(Input(times));

        Assert.Equal(SourceHealthStatus.Stale, result.Status);
    }

    [Fact]
    public void A_burst_publisher_is_not_called_stale_an_hour_later()
    {
        // Ten posts in ten minutes gives a median gap of one minute. Without a floor
        // on the derived threshold, this source would be "stale" before lunch.
        var times = Enumerable.Range(0, 10).Select(i => Now.AddMinutes(-(30 + i))).ToList();

        var result = SourceHealthEvaluator.Evaluate(Input(times));

        Assert.Equal(SourceHealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void Repeated_fetch_failures_are_reported_with_the_reason()
    {
        var result = SourceHealthEvaluator.Evaluate(
            Input(failures: 5, lastError: "HTTP 404: Not Found"));

        Assert.Equal(SourceHealthStatus.Failing, result.Status);
        Assert.Contains("404", result.Reason);
    }

    [Fact]
    public void One_failure_is_not_yet_a_broken_source()
    {
        // Feeds blip. Calling a source broken on a single timeout would make the
        // page noise.
        Assert.Equal(SourceHealthStatus.Healthy, SourceHealthEvaluator.Evaluate(Input(failures: 1)).Status);
    }

    [Fact]
    public void Polling_that_quietly_stopped_is_failing_even_with_no_recorded_error()
    {
        // No error, no failure count — the scheduler simply is not reaching it.
        var result = SourceHealthEvaluator.Evaluate(Input(
            intervalMinutes: 60,
            lastSucceeded: Now.AddDays(-10),
            lastFetched: Now.AddDays(-10)));

        Assert.Equal(SourceHealthStatus.Failing, result.Status);
        Assert.Contains("Tarama durmuş", result.Reason);
    }

    [Fact]
    public void The_schedulers_own_backoff_is_not_mistaken_for_a_stopped_crawler()
    {
        // Failures push the next attempt out by up to 24 intervals, so a gap of a
        // few intervals is the scheduler working as designed.
        var result = SourceHealthEvaluator.Evaluate(Input(
            intervalMinutes: 60,
            lastSucceeded: Now.AddHours(-20),
            lastFetched: Now.AddHours(-1)));

        Assert.Equal(SourceHealthStatus.Healthy, result.Status);
    }

    [Fact]
    public void A_disabled_source_is_not_reported_as_a_fault()
    {
        var result = SourceHealthEvaluator.Evaluate(Input(failures: 9, enabled: false));

        // Disabled outranks failing: an operator switched it off, that is not a bug
        // to chase.
        Assert.Equal(SourceHealthStatus.Disabled, result.Status);
    }

    [Fact]
    public void A_source_that_was_never_polled_is_unknown_rather_than_broken()
    {
        var result = SourceHealthEvaluator.Evaluate(new SourceHealthInput
        {
            IsEnabled = true,
            FetchIntervalMinutes = 60,
            LastFetchedAt = null,
            LastSucceededAt = null,
            ConsecutiveFailures = 0,
            NewestArticleAt = null,
            RecentArticleTimes = [],
            Now = Now
        });

        Assert.Equal(SourceHealthStatus.Unknown, result.Status);
    }

    [Fact]
    public void A_source_polled_but_never_successfully_is_failing()
    {
        var result = SourceHealthEvaluator.Evaluate(new SourceHealthInput
        {
            IsEnabled = true,
            FetchIntervalMinutes = 60,
            LastFetchedAt = Now.AddMinutes(-5),
            LastSucceededAt = null,
            ConsecutiveFailures = 1,
            NewestArticleAt = null,
            RecentArticleTimes = [],
            Now = Now
        });

        Assert.Equal(SourceHealthStatus.Failing, result.Status);
    }

    [Fact]
    public void A_source_that_fetches_fine_but_has_produced_nothing_is_stale()
    {
        var result = SourceHealthEvaluator.Evaluate(Input(times: []));

        Assert.Equal(SourceHealthStatus.Stale, result.Status);
        Assert.Contains("hiç içerik gelmedi", result.Reason);
    }

    [Fact]
    public void Cadence_needs_enough_samples_to_be_claimed()
    {
        // Two articles give one gap, which is a coincidence rather than a rhythm.
        Assert.Null(SourceHealthEvaluator.MedianGapHours(Cadence(24, 2)));
        Assert.NotNull(SourceHealthEvaluator.MedianGapHours(Cadence(24, 5)));
    }

    [Fact]
    public void Cadence_uses_the_median_so_one_burst_does_not_define_the_rhythm()
    {
        // A conference dump: seven posts in one hour, then the usual weekly rhythm.
        // The mean gap would describe neither behaviour.
        var times = new List<DateTimeOffset>();
        for (var i = 0; i < 7; i++) times.Add(Now.AddMinutes(-i * 10));
        for (var i = 1; i <= 5; i++) times.Add(Now.AddDays(-7 * i));

        var median = SourceHealthEvaluator.MedianGapHours(times);
        var mean = 0d;
        var ordered = times.OrderByDescending(t => t).ToList();
        for (var i = 0; i < ordered.Count - 1; i++) mean += (ordered[i] - ordered[i + 1]).TotalHours;
        mean /= ordered.Count - 1;

        Assert.NotNull(median);
        Assert.True(median < mean, "the median must not be dragged by the burst the way the mean is");
    }

    [Fact]
    public void Simultaneous_articles_do_not_count_as_a_zero_gap()
    {
        // A batch import writes many rows with one timestamp. Counting those as
        // zero-hour gaps would collapse the median to nothing.
        var times = Enumerable.Repeat(Now.AddHours(-1), 6)
            .Concat(Enumerable.Range(1, 6).Select(i => Now.AddDays(-i)))
            .ToList();

        var median = SourceHealthEvaluator.MedianGapHours(times);

        Assert.NotNull(median);
        Assert.True(median > 0);
    }

    [Fact]
    public void An_unmeasurable_rhythm_falls_back_to_an_absolute_limit()
    {
        // Nothing published in three months is not pulling its weight in a daily
        // digest, whatever its rhythm used to be.
        Assert.Equal(24 * 90d, SourceHealthEvaluator.StaleThresholdHours(null));

        var quiet = new List<DateTimeOffset> { Now.AddDays(-200), Now.AddDays(-400) };

        var result = SourceHealthEvaluator.Evaluate(Input(quiet));

        Assert.Equal(SourceHealthStatus.Stale, result.Status);
    }

    [Fact]
    public void The_derived_threshold_is_clamped_at_both_ends()
    {
        // One minute of rhythm cannot mean a one-hour patience...
        Assert.Equal(72d, SourceHealthEvaluator.StaleThresholdHours(1d / 60d));

        // ...and a yearly rhythm cannot mean infinite patience.
        Assert.Equal(24 * 90d, SourceHealthEvaluator.StaleThresholdHours(24 * 365d));
    }
}
