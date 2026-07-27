using FocusAI.Infrastructure.Configuration;
using Xunit;

namespace FocusAI.UnitTests.Infrastructure;

/// <summary>
/// The schedule expression, which has to parse or the API does not start.
/// </summary>
/// <remarks>
/// Scheduling runs before Kestrel listens, so an expression the cron parser refuses
/// does not produce a broken job — it produces a process that exits. That failure
/// looks like "the site is down" from every angle: no endpoints, no swagger, no
/// login, nothing in the browser pointing at a schedule. Worth a test that costs
/// nothing.
/// </remarks>
public class IngestionScheduleTests
{
    private static string Cron(int minutes) =>
        new IngestionOptions { IngestCronMinutes = minutes }.CronExpression;

    [Theory]
    [InlineData(5, "*/5 * * * *")]
    [InlineData(20, "*/20 * * * *")]
    [InlineData(59, "*/59 * * * *")]
    public void Sub_hourly_intervals_step_the_minutes_field(int minutes, string expected) =>
        Assert.Equal(expected, Cron(minutes));

    [Theory]
    [InlineData(60, "0 */1 * * *")]
    [InlineData(120, "0 */2 * * *")]
    [InlineData(240, "0 */4 * * *")]
    public void An_hour_or_more_moves_to_the_hours_field(int minutes, string expected) =>
        Assert.Equal(expected, Cron(minutes));

    [Fact]
    public void The_minutes_field_never_carries_a_step_it_cannot_hold()
    {
        // The regression. "*/60 * * * *" asks for a step of 60 over a range of
        // 0-59; Cronos rejects it and the throw takes the whole API down at
        // startup. Every value the clamp allows is checked, because the one that
        // broke it was a plausible configuration change, not an exotic one.
        for (var minutes = 1; minutes <= 300; minutes++)
        {
            var cron = Cron(minutes);
            var minuteField = cron.Split(' ')[0];

            if (!minuteField.StartsWith("*/", StringComparison.Ordinal))
            {
                continue;
            }

            var step = int.Parse(minuteField[2..]);
            Assert.InRange(step, 1, 59);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(10_000)]
    public void Nonsense_configuration_still_yields_something_parseable(int minutes)
    {
        var cron = Cron(minutes);
        var fields = cron.Split(' ');

        Assert.Equal(5, fields.Length);
        Assert.DoesNotContain("*/0", cron, StringComparison.Ordinal);
    }

    [Fact]
    public void Intervals_that_are_not_whole_hours_round_down_to_the_hour()
    {
        // Documented behaviour rather than an accident: 90 minutes becomes hourly.
        Assert.Equal("0 */1 * * *", Cron(90));
    }
}
