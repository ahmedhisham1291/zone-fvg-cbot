using System;
using System.Collections.Generic;
using System.Globalization;

namespace ZoneFvg.Core.Recovery;

/// <summary>
/// A day of the year without a year, such as 25 December, for the market holidays of REC-003
/// (owner decision of 2026-11-16). Written as <c>MM-dd</c>.
/// </summary>
/// <param name="Month">Month, 1 to 12.</param>
/// <param name="Day">Day of the month, valid for the month in a leap year.</param>
public readonly record struct MonthDay(int Month, int Day)
{
    /// <summary>
    /// Reads a comma-separated list of <c>MM-dd</c> days, for example <c>12-25,01-01</c> (REC-003).
    /// Spaces around an entry are ignored; an empty text is an empty list.
    /// </summary>
    /// <param name="text">The list.</param>
    /// <returns>The days in the order given.</returns>
    /// <exception cref="ArgumentException">An entry is not a valid <c>MM-dd</c> day.</exception>
    public static IReadOnlyList<MonthDay> ParseList(string? text)
    {
        var days = new List<MonthDay>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return days.AsReadOnly();
        }

        foreach (string raw in text.Split(','))
        {
            string entry = raw.Trim();
            if (!DateTime.TryParseExact("2000-" + entry, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date))
            {
                throw new ArgumentException("A market holiday must be written MM-dd, for example 12-25, but was \"" + entry + "\".", nameof(text));
            }

            days.Add(new MonthDay(date.Month, date.Day));
        }

        return days.AsReadOnly();
    }

    /// <summary>True when the UTC date of the time is one of the days (REC-003).</summary>
    /// <param name="days">The holidays; null means none.</param>
    /// <param name="timeUtc">A time, kind UTC.</param>
    /// <returns>True on a holiday.</returns>
    public static bool Contains(IReadOnlyCollection<MonthDay>? days, DateTime timeUtc)
    {
        if (days == null)
        {
            return false;
        }

        foreach (MonthDay day in days)
        {
            if (day.Month == timeUtc.Month && day.Day == timeUtc.Day)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The day as <c>MM-dd</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return Month.ToString("00", CultureInfo.InvariantCulture) + "-" + Day.ToString("00", CultureInfo.InvariantCulture);
    }
}
