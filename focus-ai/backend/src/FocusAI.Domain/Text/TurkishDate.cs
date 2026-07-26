using System.Text.RegularExpressions;
using FocusAI.Domain.Enums;

namespace FocusAI.Domain.Text;

/// <summary>
/// Turns a Turkish date expression from a news item into a date the feed can sort
/// and bucket by.
/// </summary>
/// <remarks>
/// Deliberately conservative: anything it cannot resolve confidently returns null,
/// and a null date keeps the item out of the certain feed entirely. Guessing a date
/// would put an item under a horizon it does not belong to, which is the one error
/// this whole section exists to avoid.
/// </remarks>
public static partial class TurkishDate
{
    private static readonly string[] Months =
    [
        "ocak", "subat", "mart", "nisan", "mayis", "haziran",
        "temmuz", "agustos", "eylul", "ekim", "kasim", "aralik"
    ];

    /// <summary>"12 Temmuz 2026" / "15 Ekim".</summary>
    [GeneratedRegex(@"\b(?<day>\d{1,2})\s+(?<month>[a-z]+)(?:\s+(?<year>\d{4}))?\b")]
    private static partial Regex DayMonthPattern();

    /// <summary>"12.07.2026" / "12/07/2026".</summary>
    [GeneratedRegex(@"\b(?<day>\d{1,2})[./](?<month>\d{1,2})[./](?<year>\d{4})\b")]
    private static partial Regex NumericPattern();

    /// <summary>"Temmuz 2026" / "Eylül ayında".</summary>
    [GeneratedRegex(@"\b(?<month>[a-z]+)\s+(?:ayinda|ayi|)\s*(?<year>\d{4})?\b")]
    private static partial Regex MonthOnlyPattern();

    /// <summary>"3. çeyrek" / "2027 yılı".</summary>
    [GeneratedRegex(@"\b(?<q>[1-4])\s*\.?\s*ceyrek\b")]
    private static partial Regex QuarterPattern();

    [GeneratedRegex(@"\b(?<year>20\d{2})\b")]
    private static partial Regex YearPattern();

    /// <summary>
    /// Resolves the expression against the article's publish date, which supplies
    /// the year when the text omits it.
    /// </summary>
    public static (DateOnly? Date, DatePrecision Precision) Parse(string? text, DateTimeOffset publishedAt)
    {
        if (CommitmentLexicon.IsVagueDate(text))
        {
            return (null, DatePrecision.None);
        }

        var folded = CommitmentLexicon.Fold(text);
        var today = DateOnly.FromDateTime(publishedAt.UtcDateTime);

        if (Numeric(folded) is { } numeric)
        {
            return (numeric, DatePrecision.Day);
        }

        if (DayMonth(folded, today) is { } dayMonth)
        {
            return (dayMonth, DatePrecision.Day);
        }

        if (Quarter(folded, today) is { } quarter)
        {
            return (quarter, DatePrecision.Window);
        }

        if (MonthOnly(folded, today) is { } month)
        {
            return (month, DatePrecision.Window);
        }

        if (YearOnly(folded) is { } year)
        {
            return (year, DatePrecision.Window);
        }

        return (null, DatePrecision.None);
    }

    private static DateOnly? Numeric(string folded)
    {
        var match = NumericPattern().Match(folded);
        return match.Success
            ? Build(int.Parse(match.Groups["year"].Value), int.Parse(match.Groups["month"].Value),
                int.Parse(match.Groups["day"].Value))
            : null;
    }

    private static DateOnly? DayMonth(string folded, DateOnly today)
    {
        foreach (Match match in DayMonthPattern().Matches(folded))
        {
            var monthIndex = Array.IndexOf(Months, match.Groups["month"].Value);
            if (monthIndex < 0)
            {
                continue;
            }

            var day = int.Parse(match.Groups["day"].Value);
            var year = match.Groups["year"].Success
                ? int.Parse(match.Groups["year"].Value)
                : RollForward(today, monthIndex + 1, day);

            return Build(year, monthIndex + 1, day);
        }

        return null;
    }

    private static DateOnly? MonthOnly(string folded, DateOnly today)
    {
        foreach (Match match in MonthOnlyPattern().Matches(folded))
        {
            var monthIndex = Array.IndexOf(Months, match.Groups["month"].Value);
            if (monthIndex < 0)
            {
                continue;
            }

            var year = match.Groups["year"].Success
                ? int.Parse(match.Groups["year"].Value)
                : RollForward(today, monthIndex + 1, 1);

            // A month-precision event is dated to its end: saying "September" and
            // then treating it as the 1st would expire the item three weeks early.
            return Build(year, monthIndex + 1, DateTime.DaysInMonth(year, monthIndex + 1));
        }

        return null;
    }

    private static DateOnly? Quarter(string folded, DateOnly today)
    {
        var match = QuarterPattern().Match(folded);
        if (!match.Success)
        {
            return null;
        }

        var quarter = int.Parse(match.Groups["q"].Value);
        var yearMatch = YearPattern().Match(folded);
        var year = yearMatch.Success ? int.Parse(yearMatch.Groups["year"].Value) : today.Year;
        var month = quarter * 3;

        return Build(year, month, DateTime.DaysInMonth(year, month));
    }

    private static DateOnly? YearOnly(string folded)
    {
        var match = YearPattern().Match(folded);
        return match.Success ? Build(int.Parse(match.Groups["year"].Value), 12, 31) : null;
    }

    /// <summary>
    /// A bare "15 Ekim" published in December means next October, not one that has
    /// already gone by.
    /// </summary>
    private static int RollForward(DateOnly today, int month, int day)
    {
        var candidate = Build(today.Year, month, day);
        return candidate is { } date && date < today ? today.Year + 1 : today.Year;
    }

    private static DateOnly? Build(int year, int month, int day)
    {
        if (year is < 1900 or > 2100 || month is < 1 or > 12 || day < 1)
        {
            return null;
        }

        return day > DateTime.DaysInMonth(year, month)
            ? null
            : new DateOnly(year, month, day);
    }
}
