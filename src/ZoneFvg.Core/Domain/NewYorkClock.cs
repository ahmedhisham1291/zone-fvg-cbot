using System;

namespace ZoneFvg.Core.Domain;

/// <summary>
/// New York wall-clock time and the trading day of the SessionRange mode (spec section 3.8,
/// SIG-030). New York time is UTC-4 from 07:00 UTC on the second Sunday of March until 06:00 UTC
/// on the first Sunday of November, and UTC-5 otherwise. A trading day starts at 17:00 New York,
/// ends at 17:00 New York on the next calendar day and is named by the date on which it ends.
/// Operating-system time-zone data is not used.
/// </summary>
/// <remarks>
/// Both switches happen at 02:00 New York time on a Sunday, which lies in the trading day named
/// by that Sunday. Wall-clock times inside a switch (02:00 to 02:59 in March, which does not
/// exist, and 01:00 to 01:59 in November, which exists twice) therefore only occur in trading
/// days that end on a Sunday. <see cref="UtcInTradingDay"/> converts such a time with the
/// summer offset when the result is in summer time and with the winter offset otherwise.
/// </remarks>
public static class NewYorkClock
{
    /// <summary>Hour of day, in UTC, at which summer time starts on the second Sunday of March (SIG-030).</summary>
    public const int SummerStartHourUtc = 7;

    /// <summary>Hour of day, in UTC, at which summer time ends on the first Sunday of November (SIG-030).</summary>
    public const int SummerEndHourUtc = 6;

    /// <summary>New York time of day at which a trading day starts (SIG-030): 17:00.</summary>
    public static readonly TimeSpan TradingDayStart = TimeSpan.FromHours(17.0);

    private static readonly TimeSpan SummerOffset = TimeSpan.FromHours(-4.0);
    private static readonly TimeSpan WinterOffset = TimeSpan.FromHours(-5.0);
    private static readonly TimeSpan OneDay = TimeSpan.FromHours(24.0);

    /// <summary>Start of summer time in a year: 07:00 UTC on the second Sunday of March (SIG-030).</summary>
    /// <param name="year">The year, from 1 to 9999.</param>
    /// <returns>The switch instant, with kind UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is outside 1 to 9999.</exception>
    public static DateTime SummerTimeStartUtc(int year)
    {
        return NthSunday(year, 3, 2, SummerStartHourUtc);
    }

    /// <summary>End of summer time in a year: 06:00 UTC on the first Sunday of November (SIG-030).</summary>
    /// <param name="year">The year, from 1 to 9999.</param>
    /// <returns>The switch instant, with kind UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The year is outside 1 to 9999.</exception>
    public static DateTime SummerTimeEndUtc(int year)
    {
        return NthSunday(year, 11, 1, SummerEndHourUtc);
    }

    /// <summary>
    /// True when New York summer time (UTC-4) is in force at the instant: from the March switch
    /// inclusive to the November switch exclusive (SIG-030).
    /// </summary>
    /// <param name="utc">The instant. Its kind must be UTC.</param>
    /// <returns>True in summer time, false in winter time.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="utc"/> is not UTC.</exception>
    public static bool IsSummerTime(DateTime utc)
    {
        RequireUtc(utc, nameof(utc));
        return utc >= SummerTimeStartUtc(utc.Year) && utc < SummerTimeEndUtc(utc.Year);
    }

    /// <summary>Offset of New York time from UTC at the instant: minus four or minus five hours (SIG-030).</summary>
    /// <param name="utc">The instant. Its kind must be UTC.</param>
    /// <returns>Minus four hours in summer time, otherwise minus five hours.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="utc"/> is not UTC.</exception>
    public static TimeSpan UtcOffset(DateTime utc)
    {
        return IsSummerTime(utc) ? SummerOffset : WinterOffset;
    }

    /// <summary>New York wall-clock time of the instant (SIG-030).</summary>
    /// <param name="utc">The instant. Its kind must be UTC.</param>
    /// <returns>The wall-clock time, with kind Unspecified.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="utc"/> is not UTC.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The wall-clock time is before the smallest date.</exception>
    public static DateTime ToNewYork(DateTime utc)
    {
        return DateTime.SpecifyKind(utc + UtcOffset(utc), DateTimeKind.Unspecified);
    }

    /// <summary>
    /// The date that names the trading day containing the instant (SIG-030): the New York date
    /// of the instant when it is before 17:00 New York, otherwise the next date.
    /// </summary>
    /// <param name="utc">The instant. Its kind must be UTC.</param>
    /// <returns>The date at 00:00, with kind Unspecified.</returns>
    /// <exception cref="ArgumentException">The kind of <paramref name="utc"/> is not UTC.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The date is outside the range of <see cref="DateTime"/>.</exception>
    public static DateTime TradingDate(DateTime utc)
    {
        // 17:00 New York plus seven hours is midnight of the date that names the trading day.
        return (ToNewYork(utc) + (OneDay - TradingDayStart)).Date;
    }

    /// <summary>
    /// The UTC instant at which the trading day named by <paramref name="tradingDate"/> starts:
    /// 17:00 New York on the date before it (SIG-030).
    /// </summary>
    /// <param name="tradingDate">The date that names the trading day; its time of day and kind are ignored.</param>
    /// <returns>21:00 UTC (summer time) or 22:00 UTC (winter time) of the date before, with kind UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The start is outside the range of <see cref="DateTime"/>.</exception>
    public static DateTime TradingDayStartUtc(DateTime tradingDate)
    {
        return UtcInTradingDay(tradingDate, TradingDayStart);
    }

    /// <summary>
    /// Place of a New York time of day in the trading day (SIG-030): its distance from 17:00, so
    /// 17:00 is 00:00, 19:00 is 02:00, 03:00 is 10:00 and 16:59 is 23:59.
    /// </summary>
    /// <param name="newYorkTimeOfDay">A time of day from 00:00 up to but not including 24:00.</param>
    /// <returns>The distance from 17:00, from 00:00 up to but not including 24:00.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The time of day is outside 00:00 up to but not including 24:00.</exception>
    public static TimeSpan PlaceInTradingDay(TimeSpan newYorkTimeOfDay)
    {
        if (newYorkTimeOfDay < TimeSpan.Zero || newYorkTimeOfDay >= OneDay)
        {
            throw new ArgumentOutOfRangeException(nameof(newYorkTimeOfDay), "The New York time of day must be from 00:00 up to but not including 24:00 but was " + Invariant.Text(newYorkTimeOfDay) + ".");
        }

        return newYorkTimeOfDay >= TradingDayStart
            ? newYorkTimeOfDay - TradingDayStart
            : newYorkTimeOfDay + (OneDay - TradingDayStart);
    }

    /// <summary>
    /// The UTC instant of a New York time of day inside the trading day named by
    /// <paramref name="tradingDate"/> (SIG-030). The time is placed by
    /// <see cref="PlaceInTradingDay"/>: 17:00 to 23:59 fall on the date before, 00:00 to 16:59 on
    /// the date itself.
    /// </summary>
    /// <param name="tradingDate">The date that names the trading day; its time of day and kind are ignored.</param>
    /// <param name="newYorkTimeOfDay">A time of day from 00:00 up to but not including 24:00.</param>
    /// <returns>The instant, with kind UTC.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The time of day is out of range, or the instant is outside the range of <see cref="DateTime"/>.</exception>
    public static DateTime UtcInTradingDay(DateTime tradingDate, TimeSpan newYorkTimeOfDay)
    {
        TimeSpan place = PlaceInTradingDay(newYorkTimeOfDay);
        DateTime wallClock = tradingDate.Date - OneDay + TradingDayStart + place;
        DateTime summerCandidate = DateTime.SpecifyKind(wallClock - SummerOffset, DateTimeKind.Utc);
        if (IsSummerTime(summerCandidate))
        {
            return summerCandidate;
        }

        return DateTime.SpecifyKind(wallClock - WinterOffset, DateTimeKind.Utc);
    }

    private static DateTime NthSunday(int year, int month, int nth, int hourUtc)
    {
        if (year < 1 || year > 9999)
        {
            throw new ArgumentOutOfRangeException(nameof(year), "The year must be between 1 and 9999 but was " + Invariant.Text(year) + ".");
        }

        var first = new DateTime(year, month, 1, hourUtc, 0, 0, DateTimeKind.Utc);
        int toSunday = ((int)DayOfWeek.Sunday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(toSunday + (7 * (nth - 1)));
    }

    private static void RequireUtc(DateTime value, string name)
    {
        if (value.Kind != DateTimeKind.Utc)
        {
            throw new ArgumentException("The time must be a UTC time (kind UTC).", name);
        }
    }
}
