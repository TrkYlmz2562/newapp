using FocusAI.Domain.Common;
using Xunit;

namespace FocusAI.UnitTests.Domain;

/// <summary>
/// Regression cover for a real production failure: once full-article extraction
/// was enabled, extracted first sentences grew past the 600-character Dek column
/// and every enrichment batch died with "value too long for type character
/// varying". Model output is never assumed to respect its length instruction.
/// </summary>
public class FieldLimitsTests
{
    [Fact]
    public void Short_values_pass_through_untouched() =>
        Assert.Equal("kısa bir özet", FieldLimits.Cap("kısa bir özet", 100));

    [Fact]
    public void Null_and_whitespace_are_preserved()
    {
        Assert.Null(FieldLimits.Cap(null, 100));
        Assert.Equal("   ", FieldLimits.Cap("   ", 100));
    }

    [Fact]
    public void Values_are_trimmed() =>
        Assert.Equal("özet", FieldLimits.Cap("  özet  ", 100));

    [Fact]
    public void Long_values_never_exceed_the_limit()
    {
        var overlong = string.Join(' ', Enumerable.Repeat("kelime", 500));

        var capped = FieldLimits.Cap(overlong, FieldLimits.StoryDek)!;

        Assert.True(
            capped.Length <= FieldLimits.StoryDek,
            $"Capped value was {capped.Length} characters, limit is {FieldLimits.StoryDek}.");
    }

    [Fact]
    public void Truncation_marks_the_cut()
    {
        var overlong = string.Join(' ', Enumerable.Repeat("kelime", 500));

        Assert.EndsWith("…", FieldLimits.Cap(overlong, 100));
    }

    [Fact]
    public void Truncation_prefers_a_word_boundary()
    {
        var capped = FieldLimits.Cap("bir iki üç dört beş altı yedi sekiz dokuz on", 20)!;

        // No half-word before the ellipsis.
        Assert.DoesNotContain("dör…", capped);
        Assert.EndsWith("…", capped);
    }

    [Fact]
    public void A_single_enormous_token_is_still_cut_to_the_limit()
    {
        // No word boundary exists, so the boundary rule must not win over the cap.
        var capped = FieldLimits.Cap(new string('x', 5000), 50)!;

        Assert.True(capped.Length <= 50);
    }

    [Fact]
    public void Value_exactly_at_the_limit_is_not_altered()
    {
        var exact = new string('a', 120);

        Assert.Equal(exact, FieldLimits.Cap(exact, 120));
    }

    [Fact]
    public void Trailing_punctuation_is_cleaned_before_the_ellipsis()
    {
        var capped = FieldLimits.Cap("birinci bölüm, ikinci bölüm, üçüncü bölüm devam ediyor", 22)!;

        Assert.DoesNotContain(",…", capped);
    }

    [Theory]
    [InlineData(FieldLimits.StoryTitle)]
    [InlineData(FieldLimits.StoryDek)]
    [InlineData(FieldLimits.Summary)]
    [InlineData(FieldLimits.SummarySection)]
    [InlineData(FieldLimits.AnalysisSection)]
    [InlineData(FieldLimits.KeyPoint)]
    [InlineData(FieldLimits.ProviderName)]
    [InlineData(FieldLimits.ModelName)]
    public void Every_declared_limit_actually_bounds_its_field(int limit)
    {
        var overlong = string.Join(' ', Enumerable.Repeat("uzunbirkelime", 4000));

        Assert.True(FieldLimits.Cap(overlong, limit)!.Length <= limit);
    }
}
