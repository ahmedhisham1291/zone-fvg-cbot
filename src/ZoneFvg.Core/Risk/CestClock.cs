using System;

namespace ZoneFvg.Core.Risk;

/// <summary>
/// Central European time with the explicit EU daylight-saving rule (RSK-012). Central European
/// time is UTC+1 (CET), or UTC+2 (CEST) from 01:00 UTC on the last Sunday of March until
/// 01:00 UTC on the last Sunday of October. Operating-system time-zone data is not used.
/// </summary>
/// <remarks>
/// The switch happens at 01:00 UTC, which is 02:00 or 03:00 local time, so local midnight is
/// never inside a switch and every CE(S)T day has exactly one start instant.
/// </remarks>
public static class CestClock
{
    /// <summary>Hour of day, in UTC, at which the clocks change on the switch Sundays (RSK-012).</summary>
    public const int SwitchHourUtc = 1;

    private static readonly TimeSpan WinterOffset = TimeSpan.FromHours(1.0);
    private static readonly TimeSpan SummerOffset = TimeSpan.FromHours(2.0);

    /// <summary>Start of summer time in a year: 01:00 UTC on the last Sunday of March (RSK-012).</summary>
    /// <param name="year">The year, from 1 to 9999.</param>
    /// <returns>The switch instant, with kind UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is outside 1 to 9999.</exception>
    public static DateTime SummerTimeStartUtc(int year)
    {
        return LastSundayAtSwitchHour(year, 3);
    }

    /// <summary>End of summer time in a year: 01:00 UTC on the last Sunday of October (RSK-012).</summary>
    /// <param name="year">The year, from 1 to 9999.</param>
    /// <returns>The switch instant, with kind UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is outside 1 to 9999.</exception>
    public static DateTime SummerTimeEndUtc(int year)
    {
        return LastSundayAtSwitchHour(year, 10);
    }

    /// <summary>
    /// True when Central European Summer Time (UTC+2) is in force at the instant: from the March
    /// switch inclusive to the October switch exclusive (RSK-012).
    /// </summary>
    /// <param name="utc">The instant. Its kind must be UTC.</param>
    /// <returns>True in summer time, false in winter time.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="utc"/> is not UTC.</exception>
    public static bool IsSummerTime(DateTime utc)
    {
        RequireUtc(utc, nameof(utc));
        return utc >= SummerTimeStartUtc(utc.Year) && utc < SummerTimeEndUtc(utc.Year);
    }

    /// <summary>Offset of Central European time from UTC at the instant: one or two hours (RSK-012).</summary>
    /// <param name="utc">The instant. Its kind must be UTC.</param>
    /// <returns>Two hours in summer time, otherwise one hour.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="utc"/> is not UTC.</exception>
    public static TimeSpan UtcOffset(DateTime utc)
    {
        return IsSummerTime(utc) ? SummerOffset : WinterOffset;
    }

    /// <summary>
    /// The instant, in UTC, at which the CE(S)T day containing <paramref name="utcNow"/> began,
    /// that is 00:00 Central European time (RSK-012, used by RSK-006, RSK-007 and RSK-010).
    /// The result is never after <paramref name="utcNow"/>.
    /// </summary>
    /// <param name="utcNow">The instant. Its kind must be UTC.</param>
    /// <returns>Local midnight as a UTC instant, with kind UTC: 23:00 UTC of the previous date in winter, 22:00 UTC in summer.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="utcNow"/> is not UTC.</exception>
    public static DateTime StartOfCestDayUtc(DateTime utcNow)
    {
        DateTime localWallClock = utcNow + UtcOffset(utcNow);
        DateTime localMidnight = localWallClock.Date;

        // Local midnight is 22:00 UTC if summer time was in force at that instant, otherwise 23:00 UTC.
        DateTime summerCandidate = localMidnight - SummerOffset;
        if (IsSummerTime(summerCandidate))
        {
            return summerCandidate;
        }

        return localMidnight - WinterOffset;
    }

    private static DateTime LastSundayAtSwitchHour(int year, int month)
    {
        if (year < 1 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "The year must be between 1 and 9999.");
        }

        int day = DateTime.DaysInMonth(year, month);
        while (new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc).DayOfWeek != DayOfWeek.Sunday)
        {
            day--;
        }

        return new DateTime(year, month, day, SwitchHourUtc, 0, 0, DateTimeKind.Utc);
    }

    private static void RequireUtc(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The time must be a UTC time (kind UTC).", name);
        }
    }
}
