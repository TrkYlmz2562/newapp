using FocusAI.Domain.Timeline;
using Xunit;

namespace FocusAI.UnitTests.Timeline;

public class StoryTimelineTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static TimelineEntry At(double hoursAgo, string source = "Kaynak", bool official = false) =>
        new(Now.AddHours(-hoursAgo), source, official);

    [Fact]
    public void No_coverage_yields_no_timeline() =>
        Assert.Null(StoryTimeline.Build([], Now));

    [Fact]
    public void A_story_that_just_broke_is_breaking()
    {
        var result = StoryTimeline.Build([At(3, "The Verge"), At(1, "Ars Technica")], Now);

        Assert.Equal(StoryPhase.Breaking, result!.Phase);
    }

    [Fact]
    public void An_older_story_still_collecting_coverage_is_developing()
    {
        var result = StoryTimeline.Build([At(72, "Reuters"), At(5, "Bloomberg")], Now);

        Assert.Equal(StoryPhase.Developing, result!.Phase);
    }

    [Fact]
    public void A_story_nothing_has_touched_for_days_is_settled()
    {
        var result = StoryTimeline.Build([At(200, "Reuters"), At(150, "Bloomberg")], Now);

        Assert.Equal(StoryPhase.Settled, result!.Phase);
    }

    [Fact]
    public void Age_alone_does_not_settle_a_story_that_is_still_moving()
    {
        // Two weeks old but an outlet filed an hour ago: the reader needs to know it
        // is live, not that it started a while back.
        var result = StoryTimeline.Build([At(336, "Reuters"), At(1, "Bloomberg")], Now);

        Assert.Equal(StoryPhase.Developing, result!.Phase);
    }

    [Fact]
    public void The_first_outlet_is_always_a_moment()
    {
        var result = StoryTimeline.Build([At(10, "Ekonomim"), At(2, "AA Ekonomi")], Now);

        var first = result!.Moments[0];
        Assert.Equal(TimelineMomentKind.FirstReport, first.Kind);
        Assert.Equal("Ekonomim", first.SourceName);
    }

    [Fact]
    public void An_official_source_arriving_later_is_the_moment_the_claim_stopped_being_a_claim()
    {
        var result = StoryTimeline.Build(
            [At(30, "Ekonomim"), At(20, "Bloomberg"), At(4, "TCMB", official: true)],
            Now);

        var confirmation = Assert.Single(
            result!.Moments.Where(m => m.Kind == TimelineMomentKind.OfficialConfirmation));

        Assert.Equal("TCMB", confirmation.SourceName);
    }

    [Fact]
    public void An_official_source_breaking_its_own_news_is_not_also_a_confirmation()
    {
        // One event, not two. A central bank announcing its own decision is the first
        // report; calling it a confirmation as well would invent a second moment.
        var result = StoryTimeline.Build(
            [At(10, "TCMB", official: true), At(8, "AA Ekonomi")],
            Now);

        Assert.DoesNotContain(result!.Moments, m => m.Kind == TimelineMomentKind.OfficialConfirmation);
        Assert.Equal("TCMB", result.Moments[0].SourceName);
    }

    [Fact]
    public void Coverage_returning_after_a_long_silence_is_called_out()
    {
        // The shape worth naming: it broke, went quiet for days, then came back.
        var result = StoryTimeline.Build(
            [At(200, "Reuters"), At(196, "Bloomberg"), At(6, "The Verge")],
            Now);

        var resurgence = Assert.Single(
            result!.Moments.Where(m => m.Kind == TimelineMomentKind.Resurgence));

        Assert.Equal("The Verge", resurgence.SourceName);
        Assert.True(result.LongestQuietHours > 24);
    }

    [Fact]
    public void A_few_hours_between_reports_is_not_a_gap()
    {
        // Ordinary pickup, not a story going quiet and returning.
        var result = StoryTimeline.Build(
            [At(12, "Reuters"), At(9, "Bloomberg"), At(6, "The Verge")],
            Now);

        Assert.DoesNotContain(result!.Moments, m => m.Kind == TimelineMomentKind.Resurgence);
    }

    [Fact]
    public void A_two_article_story_does_not_list_the_same_fact_twice()
    {
        var result = StoryTimeline.Build([At(30, "Ekonomim"), At(4, "AA Ekonomi")], Now);

        // First report and latest report — two distinct facts, two lines.
        Assert.Equal(2, result!.Moments.Count);
        Assert.Equal(TimelineMomentKind.FirstReport, result.Moments[0].Kind);
        Assert.Equal(TimelineMomentKind.LatestReport, result.Moments[1].Kind);
    }

    [Fact]
    public void A_single_report_produces_exactly_one_moment()
    {
        var result = StoryTimeline.Build([At(5, "Webrazzi")], Now);

        var only = Assert.Single(result!.Moments);
        Assert.Equal(TimelineMomentKind.FirstReport, only.Kind);
        Assert.Equal(1, result.OutletCount);
        Assert.Equal(0d, result.SpanHours);
    }

    [Fact]
    public void The_same_outlet_filing_twice_counts_once()
    {
        // One outlet posting an update is one voice. Counting articles would
        // overstate how widely the story is carried, which is the number the
        // reader uses to judge it.
        var result = StoryTimeline.Build(
            [At(10, "Bloomberg"), At(8, "Bloomberg"), At(6, "Reuters")],
            Now);

        Assert.Equal(2, result!.OutletCount);
    }

    [Fact]
    public void Outlet_names_are_matched_case_insensitively()
    {
        var result = StoryTimeline.Build([At(10, "Bloomberg"), At(8, "bloomberg")], Now);

        Assert.Equal(1, result!.OutletCount);
    }

    [Fact]
    public void Moments_come_back_in_chronological_order()
    {
        var result = StoryTimeline.Build(
            [At(200, "Reuters"), At(100, "TCMB", official: true), At(2, "AA Ekonomi")],
            Now);

        var times = result!.Moments.Select(m => m.At).ToList();
        Assert.Equal(times.OrderBy(t => t), times);
    }

    [Fact]
    public void Entries_arriving_out_of_order_are_sorted_before_anything_is_derived()
    {
        // The query orders them, but a timeline that silently depends on its input
        // being sorted is one refactor away from reporting the wrong first outlet.
        var result = StoryTimeline.Build([At(2, "Son"), At(40, "İlk"), At(20, "Orta")], Now);

        Assert.Equal("İlk", result!.Moments[0].SourceName);
        Assert.Equal(38d, result.SpanHours, 1);
    }
}
