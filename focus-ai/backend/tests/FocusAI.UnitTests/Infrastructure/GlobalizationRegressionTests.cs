using FocusAI.Domain.Text;
using FocusAI.Infrastructure.Configuration;
using FocusAI.Infrastructure.Services;
using Xunit;

namespace FocusAI.UnitTests.Infrastructure;

/// <summary>
/// Guards against re-enabling <c>InvariantGlobalization</c>, which compiles and
/// runs perfectly while silently breaking two core behaviours.
/// </summary>
public class GlobalizationRegressionTests
{
    [Fact]
    public void Turkish_characters_decompose_during_normalization()
    {
        // Under invariant globalization ICU is absent, FormD is a no-op, and this
        // returns "açik kaynak güncelleme" — so a Turkish headline never matches
        // its own de-accented form and de-duplication quietly stops working.
        Assert.Equal("acik kaynak guncelleme", TextNormalizer.Normalize("Açık Kaynak Güncelleme"));
        Assert.Equal("gunluk ozet", TextNormalizer.Normalize("Günlük Özet"));
    }

    [Fact]
    public void Named_time_zones_resolve_to_real_offsets()
    {
        var clock = new DateTimeProvider();
        var utcNoon = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

        var istanbul = clock.ToLocal(utcNoon, "Europe/Istanbul");

        // Invariant mode exposes only UTC, so every reader's 08:00 digest would
        // fire at 08:00 UTC regardless of where they actually are.
        Assert.Equal(TimeSpan.FromHours(3), istanbul.Offset);
        Assert.Equal(15, istanbul.Hour);
    }

    [Fact]
    public void Local_calendar_day_can_differ_from_the_utc_day()
    {
        var clock = new DateTimeProvider();

        // 22:00 UTC is already the next day in Istanbul — the boundary that
        // decides which daily digest a reader receives.
        var lateUtc = new DateTimeOffset(2026, 7, 26, 22, 0, 0, TimeSpan.Zero);
        var local = clock.ToLocal(lateUtc, "Europe/Istanbul");

        Assert.Equal(27, local.Day);
    }

    [Fact]
    public void Unknown_time_zone_degrades_to_utc_instead_of_throwing()
    {
        var clock = new DateTimeProvider();
        var utcNoon = new DateTimeOffset(2026, 7, 26, 12, 0, 0, TimeSpan.Zero);

        var result = clock.ToLocal(utcNoon, "Mars/Olympus_Mons");

        Assert.Equal(TimeSpan.Zero, result.Offset);
    }
}

/// <summary>Regression cover for the near-duplicate threshold calibration.</summary>
public class SimHashThresholdTests
{
    private const string Original =
        "OpenAI releases GPT-6 with major improvements to function calling and tool use for developers";

    [Fact]
    public void Lightly_edited_syndication_is_caught()
    {
        const string edited =
            "OpenAI releases GPT-6 with major improvements to function calling and tool use for engineers";

        Assert.True(SimHash.IsNearDuplicate(SimHash.Compute(Original), SimHash.Compute(edited)));
    }

    [Fact]
    public void Unrelated_stories_stay_well_clear_of_the_threshold()
    {
        const string unrelated =
            "Scientists discover a new species of deep sea coral off the coast of Chile this week";

        var distance = SimHash.HammingDistance(SimHash.Compute(Original), SimHash.Compute(unrelated));

        Assert.True(
            distance > SimHash.DefaultNearDuplicateThreshold * 2,
            $"Unrelated headlines should sit far above the threshold, measured {distance}.");
    }

    [Fact]
    public void Threshold_leaves_headroom_below_the_random_baseline()
    {
        // Random 64-bit hashes differ in ~32 bits. Anything approaching that
        // would start merging unrelated stories.
        Assert.True(SimHash.DefaultNearDuplicateThreshold < 16);
    }

    [Fact]
    public void Ingestion_user_agent_is_ascii_only()
    {
        // HTTP header values are ASCII; a Turkish word here fails every single
        // outbound feed request with "Request headers must contain only ASCII".
        var userAgent = new IngestionOptions().UserAgent;

        Assert.All(userAgent, ch => Assert.True(ch < 128, $"Non-ASCII character '{ch}' in User-Agent."));
    }
}
