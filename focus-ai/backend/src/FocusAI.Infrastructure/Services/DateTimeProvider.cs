using FocusAI.Application.Common.Interfaces;

namespace FocusAI.Infrastructure.Services;

/// <summary>
/// Time-zone aware clock. "08:00" in the PRD is the reader's 08:00, so every
/// per-day boundary in the product resolves through here rather than UTC.
/// </summary>
public sealed class DateTimeProvider : IDateTimeProvider
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly TodayIn(string timeZoneId) => DateOnly.FromDateTime(ToLocal(UtcNow, timeZoneId).DateTime);

    public DateTimeOffset ToLocal(DateTimeOffset utc, string timeZoneId)
    {
        var zone = Resolve(timeZoneId);
        return TimeZoneInfo.ConvertTime(utc, zone);
    }

    private static TimeZoneInfo Resolve(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // A stale or misspelled zone on a profile should degrade to UTC, not
            // throw in the middle of digest generation for every other user.
            return TimeZoneInfo.Utc;
        }
    }
}
