using FocusAI.Domain.Entities.Content;
using FocusAI.Domain.Entities.Gamification;
using FocusAI.Domain.Text;
using FocusAI.Infrastructure.Identity;
using Xunit;

namespace FocusAI.UnitTests.Infrastructure;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void Correct_password_verifies() =>
        Assert.True(_hasher.Verify("FocusAI2026!", _hasher.Hash("FocusAI2026!")));

    [Fact]
    public void Wrong_password_is_rejected() =>
        Assert.False(_hasher.Verify("wrong", _hasher.Hash("FocusAI2026!")));

    [Fact]
    public void Same_password_hashes_differently_each_time()
    {
        // Per-password salt: two users with the same password must not share a hash.
        Assert.NotEqual(_hasher.Hash("FocusAI2026!"), _hasher.Hash("FocusAI2026!"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-real-hash")]
    [InlineData("v1.notanumber.salt.hash")]
    [InlineData("v2.100.c2FsdA==.aGFzaA==")]
    public void Malformed_stored_hashes_are_rejected_without_throwing(string hash) =>
        Assert.False(_hasher.Verify("anything", hash));

    [Fact]
    public void Hash_records_its_work_factor_so_it_can_be_raised_later()
    {
        var parts = _hasher.Hash("FocusAI2026!").Split('.');

        Assert.Equal(4, parts.Length);
        Assert.Equal("v1", parts[0]);
        Assert.True(int.Parse(parts[1]) >= 100_000);
    }
}

public class SourceScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

    private static Source NewSource() => new()
    {
        Name = "Test",
        Slug = "test",
        WebsiteUrl = "https://example.com",
        FeedUrl = "https://example.com/feed",
        FetchIntervalMinutes = 30
    };

    [Fact]
    public void Never_fetched_source_is_due() => Assert.True(NewSource().IsDue(Now));

    [Fact]
    public void Disabled_source_is_never_due()
    {
        var source = NewSource();
        source.IsEnabled = false;

        Assert.False(source.IsDue(Now));
    }

    [Fact]
    public void Source_is_not_due_before_its_interval_elapses()
    {
        var source = NewSource();
        source.MarkSuccess(Now.AddMinutes(-10), null, null);

        Assert.False(source.IsDue(Now));
    }

    [Fact]
    public void Source_becomes_due_after_its_interval()
    {
        var source = NewSource();
        source.MarkSuccess(Now.AddMinutes(-31), null, null);

        Assert.True(source.IsDue(Now));
    }

    [Fact]
    public void Failures_back_the_schedule_off_exponentially()
    {
        var source = NewSource();
        source.MarkFailure(Now.AddMinutes(-31), "boom");
        source.MarkFailure(Now.AddMinutes(-31), "boom");

        // Two failures → 4× the interval, so 31 minutes is no longer enough.
        Assert.False(source.IsDue(Now));
        Assert.True(source.IsDue(Now.AddHours(3)));
    }

    [Fact]
    public void Success_clears_the_failure_backoff()
    {
        var source = NewSource();
        source.MarkFailure(Now.AddHours(-5), "boom");
        source.MarkFailure(Now.AddHours(-5), "boom");
        source.MarkSuccess(Now.AddMinutes(-31), "etag", "yesterday");

        Assert.Equal(0, source.ConsecutiveFailures);
        Assert.Null(source.LastError);
        Assert.Equal("etag", source.ETag);
        Assert.True(source.IsDue(Now));
    }

    [Fact]
    public void Long_error_messages_are_truncated_before_storage()
    {
        var source = NewSource();
        source.MarkFailure(Now, new string('x', 5000));

        Assert.Equal(1000, source.LastError!.Length);
    }
}

public class UserStreakTests
{
    private static readonly DateOnly Monday = new(2026, 7, 20);

    [Fact]
    public void First_activity_starts_the_streak()
    {
        var streak = new UserStreak();
        streak.RegisterActivity(Monday);

        Assert.Equal(1, streak.CurrentStreak);
        Assert.Equal(1, streak.LongestStreak);
    }

    [Fact]
    public void Consecutive_days_extend_the_streak()
    {
        var streak = new UserStreak();
        streak.RegisterActivity(Monday);
        streak.RegisterActivity(Monday.AddDays(1));
        streak.RegisterActivity(Monday.AddDays(2));

        Assert.Equal(3, streak.CurrentStreak);
    }

    [Fact]
    public void Same_day_activity_is_idempotent()
    {
        var streak = new UserStreak();
        streak.RegisterActivity(Monday);
        streak.RegisterActivity(Monday);
        streak.RegisterActivity(Monday);

        Assert.Equal(1, streak.CurrentStreak);
    }

    [Fact]
    public void A_missed_day_restarts_the_streak_but_keeps_the_record()
    {
        var streak = new UserStreak();
        streak.RegisterActivity(Monday);
        streak.RegisterActivity(Monday.AddDays(1));
        streak.RegisterActivity(Monday.AddDays(5));

        Assert.Equal(1, streak.CurrentStreak);
        Assert.Equal(2, streak.LongestStreak);
    }
}

public class TopicMatcherTests
{
    private static readonly TopicTerm DotNet = new(
        Guid.NewGuid(), "dotnet", [".NET", "dotnet", "asp net core", "entity framework"]);

    private static readonly TopicTerm Go = new(Guid.NewGuid(), "golang", ["Go", "golang"]);

    [Fact]
    public void Title_match_scores_higher_than_body_match()
    {
        var inTitle = TopicMatcher.Match("dotnet 10 released", "unrelated body", [DotNet]);
        var inBody = TopicMatcher.Match("unrelated title", "we upgraded to dotnet 10", [DotNet]);

        Assert.Equal(1.0, inTitle[0].Weight);
        Assert.Equal(0.6, inBody[0].Weight);
    }

    [Fact]
    public void Aliases_are_matched()
    {
        var matches = TopicMatcher.Match("Entity Framework Core 10 ships", null, [DotNet]);

        Assert.Single(matches);
        Assert.Equal("dotnet", matches[0].Slug);
    }

    [Fact]
    public void Short_terms_do_not_match_inside_longer_words()
    {
        // "Go" must not fire on "Google" — the classic false positive that makes
        // naive tagging useless.
        var matches = TopicMatcher.Match("Google announces new search features", null, [Go]);

        Assert.Empty(matches);
    }

    [Fact]
    public void Whole_token_still_matches_for_short_terms()
    {
        var matches = TopicMatcher.Match("Go 1.26 improves the garbage collector", null, [Go]);

        Assert.Single(matches);
    }

    [Fact]
    public void No_topics_configured_yields_no_matches() =>
        Assert.Empty(TopicMatcher.Match("anything at all", "body", []));
}
