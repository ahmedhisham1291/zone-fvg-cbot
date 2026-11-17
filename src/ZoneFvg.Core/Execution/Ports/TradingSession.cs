using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ZoneFvg.Core.Execution.Ports;

/// <summary>
/// One weekly trading session of a symbol as the broker reports it (REC-016, cTrader
/// <c>Symbol.MarketHours.Sessions</c>): from <see cref="StartDay"/> at <see cref="StartTime"/> to
/// <see cref="EndDay"/> at <see cref="EndTime"/>, UTC, start included and end excluded. A session
/// may run across midnight and across the end of the week (Saturday to Sunday). A data record
/// without behaviour; the gap check decides how to use it.
/// </summary>
/// <param name="StartDay">Day of the week on which the session starts.</param>
/// <param name="StartTime">UTC time of day at which the session starts, 00:00 to 24:00.</param>
/// <param name="EndDay">Day of the week on which the session ends.</param>
/// <param name="EndTime">UTC time of day at which the session ends, 00:00 to 24:00.</param>
public sealed record TradingSession(DayOfWeek StartDay, TimeSpan StartTime, DayOfWeek EndDay, TimeSpan EndTime)
{
    private static readonly TimeSpan OneDay = TimeSpan.FromDays(1.0);

    /// <summary>
    /// True when both days are days of the week and both times lie from 00:00 to 24:00 inclusive
    /// (REC-016). An invalid session makes the gap check ignore the whole list.
    /// </summary>
    public bool IsValid => IsDay(StartDay) && IsDay(EndDay) && IsTime(StartTime) && IsTime(EndTime);

    /// <summary>The session as <c>Sun 22:00-Mon 21:00</c> for the start log (REC-016); seconds only when not zero.</summary>
    public string Text => DayText(StartDay) + " " + TimeText(StartTime) + "-" + DayText(EndDay) + " " + TimeText(EndTime);

    /// <summary>
    /// A list of sessions for the start log (REC-016): the sessions separated by commas, or
    /// <c>none reported</c> for an empty list. A list with an invalid session ends with
    /// <c> (invalid, not used)</c>.
    /// </summary>
    /// <param name="sessions">The sessions; null is treated as empty.</param>
    /// <returns>The text.</returns>
    public static string ListText(IReadOnlyList<TradingSession?>? sessions)
    {
        if (sessions == null || sessions.Count == 0)
        {
            return "none reported";
        }

        var text = new StringBuilder();
        bool valid = true;
        foreach (TradingSession? session in sessions)
        {
            if (text.Length > 0)
            {
                text.Append(',');
            }

            text.Append(session == null ? "null" : session.Text);
            valid &= session != null && session.IsValid;
        }

        return valid ? text.ToString() : text.Append(" (invalid, not used)").ToString();
    }

    private static bool IsDay(DayOfWeek day)
    {
        return day >= DayOfWeek.Sunday && day <= DayOfWeek.Saturday;
    }

    private static bool IsTime(TimeSpan time)
    {
        return time >= TimeSpan.Zero && time <= OneDay;
    }

    private static string DayText(DayOfWeek day)
    {
        string name = day.ToString();
        return IsDay(day) ? name.Substring(0, 3) : name;
    }

    private static string TimeText(TimeSpan time)
    {
        if (!IsTime(time))
        {
            return time.ToString("c", CultureInfo.InvariantCulture);
        }

        string text = ((int)time.TotalHours).ToString("00", CultureInfo.InvariantCulture) + ":" + time.Minutes.ToString("00", CultureInfo.InvariantCulture);
        bool fraction = time.Seconds != 0 || time.Ticks % TimeSpan.TicksPerSecond != 0;
        return fraction ? text + ":" + time.Seconds.ToString("00", CultureInfo.InvariantCulture) : text;
    }
}
